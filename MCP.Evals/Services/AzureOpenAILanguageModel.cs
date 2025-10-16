using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using System.Text;
using System.Text.Json;

namespace MCP.Evals.Services;

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

        try
        {
            var deploymentName = _config.Name ?? "gpt-4o";
            var url = $"{_endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={_apiVersion}";

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

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Azure OpenAI API returned {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var messageContent = responseJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            _logger.LogDebug("Generated response of length: {Length}", messageContent?.Length ?? 0);
            return messageContent ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response with Azure OpenAI");
            throw new LanguageModelException("azure-openai", _config.Name, "Response generation failed", ex);
        }
    }

    public async Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with tools using Azure OpenAI for server: {ServerPath}", serverConfig.Path);

        try
        {
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