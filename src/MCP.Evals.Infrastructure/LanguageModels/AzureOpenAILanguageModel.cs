using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using System.Text;
using System.Text.Json;

namespace MCP.Evals.Infrastructure.LanguageModels;

/// <summary>
/// Azure OpenAI language model implementation with direct HTTP calls
/// Handles the specific Azure OpenAI URL format and API version requirements
/// </summary>
public class AzureOpenAILanguageModel : ILanguageModel
{
    private readonly HttpClient _httpClient;
    private readonly LanguageModelConfiguration _config;
    private readonly ILogger<AzureOpenAILanguageModel> _logger;
    private readonly IMcpClientService _mcpClientService;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _apiVersion;

    public AzureOpenAILanguageModel(
        HttpClient httpClient,
        IOptions<LanguageModelConfiguration> config,
        ILogger<AzureOpenAILanguageModel> logger,
        IMcpClientService mcpClientService,
        string endpoint,
        string apiKey,
        string apiVersion = "2025-01-01-preview")
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _mcpClientService = mcpClientService;
        _endpoint = endpoint.TrimEnd('/');
        _apiKey = apiKey;
        _apiVersion = apiVersion;
    }

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with Azure OpenAI model: {ModelName}", _config.Name);

        // Check if verbose mode is enabled
        var isVerbose = bool.TryParse(Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE"), out var verboseResult) && verboseResult;

        if (isVerbose)
        {
            Console.WriteLine($"[DEBUG] About to call Azure OpenAI with model: {_config.Name}");
        }

        try
        {
            var deploymentName = _config.Name ?? "gpt-4o";
            var url = $"{_endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={_apiVersion}";

            if (isVerbose)
            {
                Console.WriteLine($"[DEBUG] Azure OpenAI URL: {url}");
            }

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = _config.MaxTokens,
                temperature = _config.Temperature
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();

            // Ensure API key is not null before adding to headers
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Azure OpenAI API key is null or empty. Please set the AZURE_OPENAI_API_KEY environment variable.");
            }

            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

            if (isVerbose)
            {
                Console.WriteLine($"[DEBUG] About to call Azure OpenAI HTTP endpoint...");
            }
            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (isVerbose)
                {
                    Console.WriteLine($"[DEBUG] Azure OpenAI error response: {response.StatusCode} - {errorContent}");
                }
                throw new HttpRequestException($"Azure OpenAI API returned {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (isVerbose)
            {
                Console.WriteLine($"[DEBUG] Azure OpenAI response received, length: {responseContent.Length}");
            }

            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var messageContent = responseJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            _logger.LogDebug("Generated response of length: {Length}", messageContent?.Length ?? 0);
            return messageContent ?? "";
        }
        catch (Exception ex)
        {
            if (isVerbose)
            {
                Console.WriteLine($"[DEBUG] Exception in Azure OpenAI GenerateResponseAsync: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[DEBUG] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            _logger.LogError(ex, "Failed to generate response with Azure OpenAI");
            throw new LanguageModelException("azure-openai", _config.Name, "Response generation failed", ex);
        }
    }

    public async Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        string serverPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with tools using Azure OpenAI for server: {ServerPath}", serverPath);

        try
        {
            // Create a ServerConfiguration from the legacy serverPath
            var serverConfig = new ServerConfiguration
            {
                Transport = "stdio", // Default to stdio for backward compatibility
                Path = serverPath
            };

            var toolResponse = await _mcpClientService.ExecuteToolInteractionAsync(
                serverConfig, userPrompt, cancellationToken);

            var evaluationPrompt = $"""
                Based on the following tool interaction, provide a comprehensive response:
                
                User Query: {userPrompt}
                Tool Response: {toolResponse}
                
                Please analyze and synthesize the information from the tool response to provide a complete answer.
                """;

            return await GenerateResponseAsync(systemPrompt, evaluationPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not LanguageModelException)
        {
            _logger.LogError(ex, "Failed to generate response with tools using Azure OpenAI");
            throw new LanguageModelException("azure-openai", _config.Name, "Tool-based response generation failed", ex);
        }
    }
}