using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using OpenAI;
using OpenAI.Chat;

namespace MCP.Evals.Services;

/// <summary>
/// Provides OpenAI language model functionality
/// </summary>
public class OpenAILanguageService : ILanguageModel
{
    private readonly OpenAIClient _openAIClient;
    private readonly LanguageModelConfiguration _config;
    private readonly ILogger<OpenAILanguageService> _logger;
    private readonly IMcpClientService _mcpClientService;

    public OpenAILanguageService(
        OpenAIClient openAIClient,
        IOptions<LanguageModelConfiguration> config,
        ILogger<OpenAILanguageService> logger,
        IMcpClientService mcpClientService)
    {
        _openAIClient = openAIClient;
        _config = config.Value;
        _logger = logger;
        _mcpClientService = mcpClientService;
    }

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with OpenAI model: {ModelName}", _config.Name);

        try
        {
            var chatClient = _openAIClient.GetChatClient(_config.Name ?? "gpt-3.5-turbo");

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = _config.MaxTokens,
                Temperature = (float)_config.Temperature
            };

            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

            var content = response.Value.Content[0].Text;
            _logger.LogDebug("Generated response of length: {Length}", content.Length);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response with OpenAI");
            throw new LanguageModelException("openai", _config.Name, "Response generation failed", ex);
        }
    }

    public async Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with tools for server: {ServerPath}", serverConfig.Path);

        try
        {
            // For now, we'll use the MCP client service to handle tool interactions
            // In a more sophisticated implementation, we would integrate MCP tools
            // directly with OpenAI's function calling capabilities

            var toolResponse = await _mcpClientService.ExecuteToolInteractionAsync(
                serverConfig, userPrompt, cancellationToken);

            // Use the basic generate method to process the tool response
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
            _logger.LogError(ex, "Failed to generate response with tools");
            throw new LanguageModelException("openai", _config.Name, "Tool-based response generation failed", ex);
        }
    }
}