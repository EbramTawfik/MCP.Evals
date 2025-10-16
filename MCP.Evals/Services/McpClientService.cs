using Microsoft.Extensions.Logging;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace MCP.Evals.Services;

/// <summary>
/// Refactored MCP client service following SOLID principles
/// Orchestrates other focused services to handle MCP operations
/// </summary>
public class McpClientService : IMcpClientService
{
    private readonly ITransportResolver _transportResolver;
    private readonly ITransportFactory _transportFactory;
    private readonly IToolExecutionPlanner _toolExecutionPlanner;
    private readonly ILogger<McpClientService> _logger;
    private readonly bool _verboseLogging;

    public McpClientService(
        ITransportResolver transportResolver,
        ITransportFactory transportFactory,
        IToolExecutionPlanner toolExecutionPlanner,
        ILogger<McpClientService> logger)
    {
        _transportResolver = transportResolver;
        _transportFactory = transportFactory;
        _toolExecutionPlanner = toolExecutionPlanner;
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    /// <summary>
    /// Creates a client transport using the injected transport factory
    /// </summary>
    private async Task<IClientTransport> CreateClientTransportAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        var transportType = _transportResolver.ResolveTransportType(serverConfig);

        if (_verboseLogging)
        {
            _logger.LogInformation("Creating {TransportType} transport for server: {ServerPath}",
                transportType, serverConfig.Path ?? serverConfig.Url);
        }

        return await _transportFactory.CreateTransportAsync(transportType, serverConfig, cancellationToken);
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
            // Use transport factory to create and connect
            var clientTransport = await CreateClientTransportAsync(serverConfig, cancellationToken);
            await using var client = await McpClient.CreateAsync(clientTransport);

            // Delegate tool interaction planning and execution to focused service
            return await _toolExecutionPlanner.ExecuteToolInteractionAsync(
                client,
                serverConfig,
                prompt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool interaction with server: {ServerPath}", serverConfig.Path ?? serverConfig.Url);
            throw new McpClientException(serverConfig.Path ?? serverConfig.Url ?? "Unknown", $"Failed to execute tool interaction: {ex.Message}");
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
            // Use transport factory for test connection
            var transportType = _transportResolver.ResolveTransportType(serverConfig);
            var clientTransport = await _transportFactory.CreateTransportAsync(transportType, serverConfig, cancellationToken);

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

    /// <summary>
    /// Cleanup method for test HTTP servers
    /// </summary>
    private async Task CleanupTestHttpServerAsync()
    {
        // This is a simple cleanup - in a production system you might want to track test processes more carefully
        await Task.Delay(100); // Give the process a moment to settle
        // The process should be cleaned up by the CreateHttpTransportAsync method itself
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