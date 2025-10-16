using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace MCP.Evals.Infrastructure.MCP;

/// <summary>
/// MCP client service implementation using official ModelContextProtocol SDK
/// Follows SRP - only responsible for MCP operations
/// </summary>
public class McpClientService : IMcpClientService
{
    private readonly ILogger<McpClientService> _logger;
    private readonly bool _verboseLogging;
    private List<Process>? _runningServerProcesses;

    public McpClientService(
        ILogger<McpClientService> logger)
    {
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("VERBOSE") == "true";
    }

    public async Task<string> ExecuteToolInteractionAsync(
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (_verboseLogging)
            _logger.LogInformation("Starting tool interaction with server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path, serverConfig.Transport);

        try
        {
            // Resolve to absolute path to handle relative paths and mixed separators
            // For HTTP transport, path might be null (direct URL connection)
            var absoluteServerPath = !string.IsNullOrEmpty(serverConfig.Path)
                ? Path.GetFullPath(serverConfig.Path)
                : string.Empty;

            if (_verboseLogging && !string.IsNullOrEmpty(absoluteServerPath))
                _logger.LogInformation("Resolved server path: {ServerPath} -> {AbsolutePath}",
                    serverConfig.Path, absoluteServerPath);

            IClientTransport clientTransport;

            // Create transport based on server configuration
            if (serverConfig.Transport == "http")
            {
                // For HTTP transport, validate that URL is provided
                if (string.IsNullOrEmpty(serverConfig.Url))
                {
                    throw new ArgumentException("HTTP transport requires a 'url' field to be specified in the server configuration.");
                }

                if (!Uri.TryCreate(serverConfig.Url, UriKind.Absolute, out var serverUri) ||
                    (serverUri.Scheme != "http" && serverUri.Scheme != "https"))
                {
                    throw new ArgumentException($"Invalid HTTP URL: {serverConfig.Url}. Must be a valid http:// or https:// URL.");
                }

                if (string.IsNullOrEmpty(serverConfig.Path))
                {
                    // Direct URL connection - server should already be running
                    clientTransport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = "HttpDirectConnection",
                        Endpoint = serverUri
                    });

                    if (_verboseLogging)
                        _logger.LogInformation("Using direct HTTP connection to: {Url}", serverConfig.Url);
                }
                else
                {
                    // Server startup + HTTP connection
                    if (_verboseLogging)
                        _logger.LogInformation("Starting HTTP server from: {ServerPath} and connecting to: {Url}",
                            absoluteServerPath, serverConfig.Url);

                    // Start the server process
                    var serverProcess = await StartHttpServerAsync(absoluteServerPath, serverConfig);

                    // Wait for server to be ready
                    var isReady = await WaitForHttpServerAsync(serverConfig.Url, cancellationToken);
                    if (!isReady)
                    {
                        serverProcess?.Kill();
                        throw new McpClientException(absoluteServerPath, "HTTP server failed to start or become ready");
                    }

                    // Create HTTP transport
                    clientTransport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Endpoint = serverUri
                    });

                    // Store server process for cleanup later
                    _runningServerProcesses ??= new List<Process>();
                    _runningServerProcesses.Add(serverProcess);

                    if (_verboseLogging)
                        _logger.LogInformation("HTTP server started and connected successfully");
                }
            }
            else // stdio transport (default)
            {
                // Determine server type based on path/extension
                var isNodeServer = absoluteServerPath.Contains("typescript-sample") ||
                                  absoluteServerPath.EndsWith(".ts") ||
                                  absoluteServerPath.EndsWith(".js");

                var isCSharpExecutable = absoluteServerPath.EndsWith(".exe") ||
                                       absoluteServerPath.Contains("CSharpMcpSample");

                if (isNodeServer)
                {
                    // TypeScript server via npx
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "TypeScriptServer",
                        Command = "npx",
                        Arguments = ["tsx", absoluteServerPath]
                    });
                }
                else if (isCSharpExecutable)
                {
                    // C# executable - run directly
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "CSharpServer",
                        Command = absoluteServerPath,
                        Arguments = serverConfig.Args ?? []
                    });
                }
                else
                {
                    // Default: assume it's an executable
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "DefaultServer",
                        Command = absoluteServerPath,
                        Arguments = serverConfig.Args ?? []
                    });
                }
            }

            if (_verboseLogging)
            {
                var serverType = serverConfig.Transport == "http" ? "HTTP" :
                               absoluteServerPath.EndsWith(".exe") || absoluteServerPath.Contains("CSharpMcpSample") ? "C# Executable" :
                               absoluteServerPath.EndsWith(".ts") || absoluteServerPath.EndsWith(".js") ? "TypeScript" : "Default";
                _logger.LogInformation("Creating MCP client with transport for {ServerType} server", serverType);
            }

            await using var client = await McpClient.CreateAsync(clientTransport);

            // Get available tools
            var tools = await client.ListToolsAsync();
            if (_verboseLogging)
            {
                _logger.LogInformation("Available tools:");
                foreach (var tool in tools)
                {
                    _logger.LogInformation("  - {ToolName}: {Description}", tool.Name, tool.Description);
                }
            }

            // Use AI to determine which tools to call based on the prompt
            var responses = new List<string>();

            // Analyze the prompt to determine which tools to call
            var toolExecutions = await DetermineToolExecutionsAsync(prompt, tools);

            if (_verboseLogging)
                _logger.LogInformation("AI determined {Count} tool executions", toolExecutions.Count);

            // Execute the determined tool calls
            foreach (var execution in toolExecutions)
            {
                try
                {
                    if (_verboseLogging)
                        _logger.LogInformation("Calling tool {ToolName} with arguments: {Args}",
                            execution.ToolName, JsonSerializer.Serialize(execution.Arguments));

                    var result = await client.CallToolAsync(
                        execution.ToolName,
                        execution.Arguments,
                        cancellationToken: cancellationToken);

                    var response = ExtractTextFromResult(result);

                    // Only add meaningful tool responses, not metadata
                    if (!string.IsNullOrEmpty(response) && response != "No text content" && response != "Unable to extract response content")
                    {
                        responses.Add(response);
                    }

                    if (_verboseLogging)
                        _logger.LogInformation("Tool {ToolName} executed successfully", execution.ToolName);
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error calling tool {execution.ToolName}: {ex.Message}";
                    responses.Add(errorMsg);
                    _logger.LogWarning(ex, "Failed to call tool {ToolName}", execution.ToolName);
                }
            }

            // If no tools were called, provide a brief response
            if (toolExecutions.Count == 0)
            {
                responses.Add("No appropriate tools were found for this request.");
            }

            // Return only the actual tool results, not metadata
            var finalResponse = responses.Count > 0 ? string.Join("\n", responses) : "No tool responses generated.";

            if (_verboseLogging)
                _logger.LogInformation("Tool interaction completed successfully with response: {Response}", finalResponse);

            return finalResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool interaction with server: {ServerPath}", serverConfig.Path ?? serverConfig.Url);
            throw new McpClientException(serverConfig.Path ?? serverConfig.Url ?? "Unknown", $"Failed to execute tool interaction: {ex.Message}");
        }
    }

    private async Task<List<ToolExecution>> DetermineToolExecutionsAsync(string prompt, IList<ModelContextProtocol.Client.McpClientTool> availableTools)
    {
        try
        {
            // Create a system prompt that describes available tools and asks AI to determine what to call
            var toolDescriptions = availableTools.Select(t =>
                $"- {t.Name}: {t.Description ?? "No description available"}"
            );

            var systemPrompt = $@"You are an AI assistant that determines which tools to call based on user prompts.

Available tools:
{string.Join("\n", toolDescriptions)}

Based on the user's prompt, determine which tools should be called and with what parameters.
Return a JSON object with a single tool execution in this format:
{{
  ""toolName"": ""tool_name"",
  ""arguments"": {{ ""param1"": ""value1"", ""param2"": ""value2"" }}
}}

If no tools should be called, return: {{}}

Rules:
1. Only call tools that are directly relevant to the prompt
2. Use appropriate parameter values based on the prompt content
3. For mathematical operations (add, multiply), extract numbers from the prompt and use parameters 'a' and 'b'
4. For echo tools, use parameter 'message' with the text to echo
5. Be precise with parameter names and types - use 'a' and 'b' for math tools, 'message' for echo tools";

            var userPrompt = $"User prompt: {prompt}";

            // Use a simple HTTP client to call Azure OpenAI (or any OpenAI-compatible endpoint)
            var toolDecisions = await CallLlmForToolDecisionAsync(systemPrompt, userPrompt);

            if (_verboseLogging)
                _logger.LogInformation("LLM tool decision response: {Response}", toolDecisions);

            // Parse the response to get tool executions
            return ParseToolExecutions(toolDecisions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine tool executions using AI, falling back to simple logic");

            // Fallback to simple prompt-based logic
            return await DetermineToolExecutionsFallback(prompt, availableTools);
        }
    }

    private async Task<string> CallLlmForToolDecisionAsync(string systemPrompt, string userPrompt)
    {
        // Check if we have Azure OpenAI configuration
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o";

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Azure OpenAI configuration not found in environment variables");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 500,
            temperature = 0.1,
            response_format = new { type = "json_object" }
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version=2024-06-01", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();

        if (_verboseLogging)
            _logger.LogInformation("Azure OpenAI response received, length: {Length}", responseJson.Length);

        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
        var assistantMessage = responseObj.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        return assistantMessage ?? "[]";
    }

    private List<ToolExecution> ParseToolExecutions(string llmResponse)
    {
        try
        {
            // The response should be a JSON object with tool executions
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(llmResponse);

            var executions = new List<ToolExecution>();

            // Handle different response formats
            if (jsonDoc.ValueKind == JsonValueKind.Object)
            {
                // Check if it's a single tool execution (direct format)
                if (jsonDoc.TryGetProperty("toolName", out var singleToolNameProp) &&
                    jsonDoc.TryGetProperty("arguments", out var singleArgsProp))
                {
                    var toolName = singleToolNameProp.GetString();
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        var arguments = new Dictionary<string, object?>();

                        foreach (var prop in singleArgsProp.EnumerateObject())
                        {
                            arguments[prop.Name] = ExtractJsonValue(prop.Value);
                        }

                        executions.Add(new ToolExecution
                        {
                            ToolName = toolName,
                            Arguments = arguments
                        });
                    }
                }
                // Check if it's wrapped in a tools property
                else if (jsonDoc.TryGetProperty("tools", out var toolsProp))
                {
                    foreach (var item in toolsProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("toolName", out var toolNameProp) &&
                            item.TryGetProperty("arguments", out var argsProp))
                        {
                            var toolName = toolNameProp.GetString();
                            if (!string.IsNullOrEmpty(toolName))
                            {
                                var arguments = new Dictionary<string, object?>();

                                foreach (var prop in argsProp.EnumerateObject())
                                {
                                    arguments[prop.Name] = ExtractJsonValue(prop.Value);
                                }

                                executions.Add(new ToolExecution
                                {
                                    ToolName = toolName,
                                    Arguments = arguments
                                });
                            }
                        }
                    }
                }
            }
            // Handle direct array format
            else if (jsonDoc.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jsonDoc.EnumerateArray())
                {
                    if (item.TryGetProperty("toolName", out var toolNameProp) &&
                        item.TryGetProperty("arguments", out var argsProp))
                    {
                        var toolName = toolNameProp.GetString();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            var arguments = new Dictionary<string, object?>();

                            foreach (var prop in argsProp.EnumerateObject())
                            {
                                arguments[prop.Name] = ExtractJsonValue(prop.Value);
                            }

                            executions.Add(new ToolExecution
                            {
                                ToolName = toolName,
                                Arguments = arguments
                            });
                        }
                    }
                }
            }

            return executions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM tool execution response: {Response}", llmResponse);
            return new List<ToolExecution>();
        }
    }

    private static object? ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private async Task<List<ToolExecution>> DetermineToolExecutionsFallback(string prompt, IList<ModelContextProtocol.Client.McpClientTool> availableTools)
    {
        try
        {
            // Use a simpler local LLM approach or basic AI logic for fallback
            // This still uses AI but with simpler prompting and error handling
            var toolDescriptions = availableTools.Select(t =>
                $"- {t.Name}: {t.Description ?? "No description available"}"
            );

            var systemPrompt = $@"You are an AI that selects tools based on user prompts. 

Available tools:
{string.Join("\n", toolDescriptions)}

Analyze the user prompt and determine which tool(s) to call. Extract relevant parameters from the prompt.

Return JSON in this exact format:
{{
  ""tools"": [
    {{
      ""toolName"": ""exact_tool_name"",
      ""arguments"": {{
        ""param1"": ""value1"",
        ""param2"": value2
      }}
    }}
  ]
}}

Rules:
- Use exact tool names from the available list
- Extract numbers, text, or other values from the prompt for arguments
- If unsure about arguments, use reasonable defaults
- If no tools match, return empty array: {{""tools"": []}}";

            var userPrompt = $"User prompt: {prompt}";

            // Try to use OpenAI compatible API with simpler error handling
            var response = await CallLlmForToolDecisionAsync(systemPrompt, userPrompt);
            var parsed = ParseToolExecutions(response);

            if (parsed.Any())
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            if (_verboseLogging)
                _logger.LogWarning(ex, "Fallback LLM call failed, using basic text matching");
        }

        // Final fallback: basic keyword matching without hardcoded tools
        return DetermineToolExecutionsBasicFallback(prompt, availableTools);
    }

    private List<ToolExecution> DetermineToolExecutionsBasicFallback(string prompt, IList<ModelContextProtocol.Client.McpClientTool> availableTools)
    {
        var executions = new List<ToolExecution>();
        var promptLower = prompt.ToLowerInvariant();

        // Dynamic tool matching based on tool names and descriptions
        foreach (var tool in availableTools)
        {
            var toolNameLower = tool.Name.ToLowerInvariant();
            var toolDescLower = tool.Description?.ToLowerInvariant() ?? "";

            // Check if prompt mentions the tool name directly
            if (promptLower.Contains(toolNameLower))
            {
                var arguments = new Dictionary<string, object?>();

                // Try to extract common parameter patterns
                ExtractArgumentsFromPrompt(prompt, arguments);

                executions.Add(new ToolExecution
                {
                    ToolName = tool.Name,
                    Arguments = arguments
                });
                break; // Only execute one tool in basic fallback
            }

            // Check if prompt contains keywords from tool description
            if (!string.IsNullOrEmpty(toolDescLower))
            {
                var descWords = toolDescLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var matchingWords = descWords.Where(word =>
                    word.Length > 3 && promptLower.Contains(word)).Count();

                if (matchingWords >= 2) // Require at least 2 matching words
                {
                    var arguments = new Dictionary<string, object?>();
                    ExtractArgumentsFromPrompt(prompt, arguments);

                    executions.Add(new ToolExecution
                    {
                        ToolName = tool.Name,
                        Arguments = arguments
                    });
                    break; // Only execute one tool in basic fallback
                }
            }
        }

        return executions;
    }

    private void ExtractArgumentsFromPrompt(string prompt, Dictionary<string, object?> arguments)
    {
        // Extract numbers for mathematical operations
        var numbers = ExtractNumbers(prompt);
        if (numbers.Count >= 2)
        {
            arguments["a"] = numbers[0];
            arguments["b"] = numbers[1];
        }
        else if (numbers.Count == 1)
        {
            arguments["value"] = numbers[0];
            arguments["number"] = numbers[0];
        }

        // Extract text for message-based operations
        var promptWords = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (promptWords.Length > 0)
        {
            arguments["message"] = prompt;
            arguments["text"] = prompt;
            arguments["input"] = prompt;
        }

        // Look for quoted strings
        var quotedMatches = System.Text.RegularExpressions.Regex.Matches(prompt, @"['""]([^'""]*)['""]");
        if (quotedMatches.Count > 0)
        {
            arguments["message"] = quotedMatches[0].Groups[1].Value;
            arguments["text"] = quotedMatches[0].Groups[1].Value;
        }
    }

    private static List<double> ExtractNumbers(string text)
    {
        var numbers = new List<double>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (double.TryParse(word.Trim('.', ',', '!', '?'), out var number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private class ToolExecution
    {
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object?> Arguments { get; set; } = new();
    }

    private static string ExtractTextFromResult(CallToolResult result)
    {
        try
        {
            // Try to get text content first - the structure might be different in the official SDK
            var textContent = result.Content.FirstOrDefault(c => c.Type == "text");
            if (textContent != null)
            {
                // Try to access the text property - it might be under a different name
                if (textContent is TextContentBlock textBlock)
                {
                    return textBlock.Text ?? "No text content";
                }

                // Fallback: serialize the content block to see its structure
                return JsonSerializer.Serialize(textContent, new JsonSerializerOptions { WriteIndented = false });
            }

            // If no text content, serialize the entire content as JSON
            return JsonSerializer.Serialize(result.Content, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch (Exception)
        {
            return "Unable to extract response content";
        }
    }

    public async Task<bool> TestConnectionAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        if (_verboseLogging)
            _logger.LogDebug("Testing connection to server: {ServerPath} (Transport: {Transport})",
                serverConfig.Path, serverConfig.Transport);

        try
        {
            // Resolve to absolute path to handle relative paths and mixed separators
            // For HTTP transport, path might be null (direct URL connection)
            var absoluteServerPath = !string.IsNullOrEmpty(serverConfig.Path)
                ? Path.GetFullPath(serverConfig.Path)
                : string.Empty;

            if (_verboseLogging && !string.IsNullOrEmpty(absoluteServerPath))
                _logger.LogDebug("Resolved server path: {ServerPath} -> {AbsolutePath}",
                    serverConfig.Path, absoluteServerPath);

            IClientTransport clientTransport;

            // Create transport based on server configuration
            if (serverConfig.Transport == "http")
            {
                // For HTTP transport, validate that URL is provided
                if (string.IsNullOrEmpty(serverConfig.Url))
                {
                    if (_verboseLogging)
                        _logger.LogWarning("HTTP transport requires a 'url' field to be specified.");
                    return false;
                }

                if (!Uri.TryCreate(serverConfig.Url, UriKind.Absolute, out var serverUri) ||
                    (serverUri.Scheme != "http" && serverUri.Scheme != "https"))
                {
                    if (_verboseLogging)
                        _logger.LogWarning("Invalid HTTP URL: {Url}", serverConfig.Url);
                    return false;
                }

                if (string.IsNullOrEmpty(serverConfig.Path))
                {
                    // Direct HTTP connection (server already running)
                    clientTransport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = "TestHttpServer",
                        Endpoint = serverUri
                    });

                    if (_verboseLogging)
                        _logger.LogInformation("Testing direct HTTP connection to: {Url}", serverConfig.Url);
                }
                else
                {
                    // HTTP transport with server startup
                    if (_verboseLogging)
                        _logger.LogInformation("Starting HTTP server from: {ServerPath} and testing connection to: {Url}",
                            absoluteServerPath, serverConfig.Url);

                    // Start the server process
                    var serverProcess = await StartHttpServerAsync(absoluteServerPath, serverConfig);

                    try
                    {
                        // Wait for server to be ready
                        var isReady = await WaitForHttpServerAsync(serverConfig.Url, cancellationToken);
                        if (!isReady)
                        {
                            serverProcess?.Kill();
                            if (_verboseLogging)
                                _logger.LogWarning("HTTP server failed to start or become ready");
                            return false;
                        }

                        // Create HTTP transport
                        clientTransport = new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Name = "TestHttpServer",
                            Endpoint = serverUri
                        });

                        if (_verboseLogging)
                            _logger.LogInformation("HTTP server started successfully for testing");
                    }
                    catch (Exception ex)
                    {
                        serverProcess?.Kill();
                        if (_verboseLogging)
                            _logger.LogWarning(ex, "Failed to start HTTP server for testing");
                        return false;
                    }
                    finally
                    {
                        // Clean up the test server process
                        try
                        {
                            if (serverProcess != null && !serverProcess.HasExited)
                            {
                                serverProcess.Kill();
                                serverProcess.WaitForExit(2000); // Wait up to 2 seconds for cleanup
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_verboseLogging)
                                _logger.LogDebug(ex, "Error during test server cleanup");
                        }
                    }
                }
            }
            else // stdio transport (default)
            {
                // Determine server type based on path/extension
                var isNodeServer = absoluteServerPath.Contains("typescript-sample") ||
                                  absoluteServerPath.EndsWith(".ts") ||
                                  absoluteServerPath.EndsWith(".js");

                var isCSharpExecutable = absoluteServerPath.EndsWith(".exe") ||
                                       absoluteServerPath.Contains("CSharpMcpSample");

                if (isNodeServer)
                {
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "TestTypeScriptServer",
                        Command = "npx",
                        Arguments = ["tsx", absoluteServerPath]
                    });
                }
                else if (isCSharpExecutable)
                {
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "TestCSharpServer",
                        Command = absoluteServerPath,
                        Arguments = serverConfig.Args ?? []
                    });
                }
                else
                {
                    clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = "TestDefaultServer",
                        Command = absoluteServerPath,
                        Arguments = serverConfig.Args ?? []
                    });
                }
            }

            await using var client = await McpClient.CreateAsync(clientTransport);
            var tools = await client.ListToolsAsync();

            if (_verboseLogging)
                _logger.LogDebug("Connection test successful for server: {ServerPath}, found {ToolCount} tools",
                    serverConfig.Path, tools.Count);

            return tools.Any(); // Consider connection successful if we can list tools
        }
        catch (Exception ex)
        {
            if (_verboseLogging)
                _logger.LogWarning(ex, "Connection test failed for server: {ServerPath}", serverConfig.Path);
            return false;
        }
    }

    // Legacy overloads for backward compatibility
    [Obsolete("Use overload with ServerConfiguration instead")]
    public async Task<string> ExecuteToolInteractionAsync(
        string serverPath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        // Convert to ServerConfiguration and call the new method
        var serverConfig = new ServerConfiguration
        {
            Transport = "stdio",
            Path = serverPath
        };
        return await ExecuteToolInteractionAsync(serverConfig, prompt, cancellationToken);
    }

    [Obsolete("Use overload with ServerConfiguration instead")]
    public async Task<bool> TestConnectionAsync(
        string serverPath,
        CancellationToken cancellationToken = default)
    {
        // Convert to ServerConfiguration and call the new method
        var serverConfig = new ServerConfiguration
        {
            Transport = "stdio",
            Path = serverPath
        };
        return await TestConnectionAsync(serverConfig, cancellationToken);
    }

    /// <summary>
    /// Starts an HTTP server process based on the file type and configuration
    /// </summary>
    private async Task<Process> StartHttpServerAsync(string serverPath, ServerConfiguration serverConfig)
    {
        var startInfo = new ProcessStartInfo();

        if (serverPath.EndsWith(".ts") || serverPath.EndsWith(".js"))
        {
            // TypeScript/JavaScript server - use cmd to run npx on Windows
            startInfo.FileName = "cmd";
            startInfo.Arguments = $"/c npx tsx \"{Path.GetFullPath(serverPath)}\"";

            // Add port argument if specified
            if (serverConfig.Args != null && serverConfig.Args.Length > 0)
            {
                startInfo.Arguments += " " + string.Join(" ", serverConfig.Args);
            }
        }
        else if (serverPath.EndsWith(".exe"))
        {
            // C# executable
            startInfo.FileName = serverPath;
            if (serverConfig.Args != null && serverConfig.Args.Length > 0)
            {
                startInfo.Arguments = string.Join(" ", serverConfig.Args);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported server type for HTTP transport: {serverPath}");
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(serverPath)) ?? Directory.GetCurrentDirectory();

        var process = new Process { StartInfo = startInfo };

        if (_verboseLogging)
        {
            _logger.LogInformation("Starting HTTP server process: {FileName} {Arguments}",
                startInfo.FileName, startInfo.Arguments);
        }

        process.Start();

        // Give the process a moment to start
        await Task.Delay(1000);

        if (process.HasExited)
        {
            throw new McpClientException(serverPath, $"Server process exited immediately with code: {process.ExitCode}");
        }

        return process;
    }

    /// <summary>
    /// Waits for an HTTP server to become available at the specified URL
    /// </summary>
    private async Task<bool> WaitForHttpServerAsync(string url, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);

        var maxAttempts = 15; // 30 seconds total with 2s intervals
        var attempt = 0;

        while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_verboseLogging && attempt > 0)
                    _logger.LogInformation("Checking HTTP server readiness (attempt {Attempt}/{MaxAttempts}): {Url}",
                        attempt + 1, maxAttempts, url);

                // Try to connect to the server
                await httpClient.PostAsync(url,
                    new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}",
                    Encoding.UTF8, "application/json"), cancellationToken);

                // If we get any response (even error), server is up
                if (_verboseLogging)
                    _logger.LogInformation("HTTP server is ready at: {Url}", url);

                return true;
            }
            catch (Exception ex) when (attempt < maxAttempts - 1)
            {
                if (_verboseLogging)
                    _logger.LogDebug(ex, "Server not ready yet: {Error}", ex.Message);

                await Task.Delay(2000, cancellationToken);
                attempt++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to HTTP server at {Url}: {Error}", url, ex.Message);
                return false;
            }
        }

        return false;
    }
}