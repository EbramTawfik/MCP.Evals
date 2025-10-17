using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using OpenAI;
using OpenAI.Chat;

namespace MCP.Evals.Services.LanguageModels;

/// <summary>
/// Provides OpenAI language model functionality
/// </summary>
public class OpenAILanguageService : ILanguageModel
{
    private readonly OpenAIClient _openAIClient;
    private readonly LanguageModelConfiguration _config;
    private readonly ILogger<OpenAILanguageService> _logger;

    public OpenAILanguageService(
        OpenAIClient openAIClient,
        IOptions<LanguageModelConfiguration> config,
        ILogger<OpenAILanguageService> logger)
    {
        _openAIClient = openAIClient;
        _config = config.Value;
        _logger = logger;
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
            // TODO: Tool execution should be handled by a higher-level orchestrator
            // For now, just generate a basic response without tool interaction
            var enhancedPrompt = $"""
                {userPrompt}
                
                Note: This query may benefit from tool interactions, but tool execution is currently handled at a higher level.
                """;

            return await GenerateResponseAsync(systemPrompt, enhancedPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not LanguageModelException)
        {
            _logger.LogError(ex, "Failed to generate response with tools");
            throw new LanguageModelException("openai", _config.Name, "Tool-based response generation failed", ex);
        }
    }
}