using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text;

namespace MCP.Evals.Services;

/// <summary>
/// Plans tool executions based on prompts and available tools using AI
/// </summary>
public class ToolExecutionPlanningService : IToolExecutionPlanningService
{
    private readonly ILogger<ToolExecutionPlanningService> _logger;
    private readonly bool _verboseLogging;

    public ToolExecutionPlanningService(ILogger<ToolExecutionPlanningService> logger)
    {
        _logger = logger;
        _verboseLogging = Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE") == "true";
    }

    public async Task<IReadOnlyList<ToolExecution>> PlanToolExecutionsAsync(
        string prompt,
        IList<ModelContextProtocol.Client.McpClientTool> availableTools,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var executions = await PlanWithAIAsync(prompt, availableTools, cancellationToken);

            if (_verboseLogging)
            {
                _logger.LogInformation("AI determined {Count} tool executions", executions.Count);
            }

            return executions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI planning failed, falling back to pattern matching");
            return PlanWithPatternMatching(prompt, availableTools);
        }
    }

    private async Task<IReadOnlyList<ToolExecution>> PlanWithAIAsync(
        string prompt,
        IList<ModelContextProtocol.Client.McpClientTool> availableTools,
        CancellationToken cancellationToken)
    {
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

        var response = await CallAzureOpenAIAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseToolExecutions(response);
    }

    private async Task<string> CallAzureOpenAIAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
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

        var response = await client.PostAsync(
            $"{endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version=2024-06-01",
            content, cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
        var assistantMessage = responseObj.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        return assistantMessage ?? "{}";
    }

    private IReadOnlyList<ToolExecution> ParseToolExecutions(string aiResponse)
    {
        try
        {
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(aiResponse);
            var executions = new List<ToolExecution>();

            if (jsonDoc.ValueKind == JsonValueKind.Object &&
                jsonDoc.TryGetProperty("toolName", out var toolNameProp) &&
                jsonDoc.TryGetProperty("arguments", out var argsProp))
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

            return executions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI tool execution response: {Response}", aiResponse);
            return Array.Empty<ToolExecution>();
        }
    }

    private IReadOnlyList<ToolExecution> PlanWithPatternMatching(
        string prompt,
        IList<ModelContextProtocol.Client.McpClientTool> availableTools)
    {
        var promptLower = prompt.ToLowerInvariant();

        foreach (var tool in availableTools)
        {
            var toolNameLower = tool.Name.ToLowerInvariant();

            if (promptLower.Contains(toolNameLower))
            {
                var arguments = ExtractArgumentsFromPrompt(prompt);
                return new[]
                {
                    new ToolExecution
                    {
                        ToolName = tool.Name,
                        Arguments = arguments
                    }
                };
            }
        }

        return Array.Empty<ToolExecution>();
    }

    private static Dictionary<string, object?> ExtractArgumentsFromPrompt(string prompt)
    {
        var arguments = new Dictionary<string, object?>();

        // Extract numbers for mathematical operations
        var numbers = ExtractNumbers(prompt);
        if (numbers.Count >= 2)
        {
            arguments["a"] = numbers[0];
            arguments["b"] = numbers[1];
        }

        // Extract quoted text for message operations
        var quotedMatches = System.Text.RegularExpressions.Regex.Matches(prompt, @"['""]([^'""]*)['""]");
        if (quotedMatches.Count > 0)
        {
            arguments["message"] = quotedMatches[0].Groups[1].Value;
        }
        else
        {
            arguments["message"] = prompt;
        }

        return arguments;
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

    /// <summary>
    /// Executes tool interactions by analyzing prompt, planning execution, and running tools
    /// </summary>
    public async Task<string> ExecuteToolInteractionAsync(
        McpClient client,
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
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
            var toolExecutions = await PlanToolExecutionsAsync(prompt, tools, cancellationToken);

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
            _logger.LogError(ex, "Error in tool execution planning");
            throw;
        }
    }

    /// <summary>
    /// Extracts text content from MCP tool result
    /// </summary>
    private static string ExtractTextFromResult(CallToolResult result)
    {
        try
        {
            if (result?.Content == null || !result.Content.Any())
                return "No text content";

            // Try to get text content first
            var textContent = result.Content.FirstOrDefault(c => c.Type == "text");
            if (textContent != null)
            {
                // Try to access the text property using TextContentBlock
                if (textContent is TextContentBlock textBlock)
                {
                    return textBlock.Text ?? "No text content";
                }

                // Fallback: serialize the content block
                return JsonSerializer.Serialize(textContent, new JsonSerializerOptions { WriteIndented = false });
            }

            return "No text content found";
        }
        catch (Exception)
        {
            return "Unable to extract response content";
        }
    }
}