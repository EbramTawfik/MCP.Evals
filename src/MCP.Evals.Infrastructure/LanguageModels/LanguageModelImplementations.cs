using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using OpenAI;
using OpenAI.Chat;
using Anthropic.SDK;

namespace MCP.Evals.Infrastructure.LanguageModels;

/// <summary>
/// OpenAI language model implementation following LSP
/// Can be substituted with any other ILanguageModel implementation
/// </summary>
public class OpenAILanguageModel : ILanguageModel
{
    private readonly OpenAIClient _openAIClient;
    private readonly LanguageModelConfiguration _config;
    private readonly ILogger<OpenAILanguageModel> _logger;
    private readonly IMcpClientService _mcpClientService;

    public OpenAILanguageModel(
        OpenAIClient openAIClient,
        IOptions<LanguageModelConfiguration> config,
        ILogger<OpenAILanguageModel> logger,
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
        _logger.LogDebug("Generating response with OpenAI model: {ModelName}", _config.ModelName);

        try
        {
            var chatClient = _openAIClient.GetChatClient(_config.ModelName ?? "gpt-3.5-turbo");

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
            throw new LanguageModelException("openai", _config.ModelName, "Response generation failed", ex);
        }
    }

    public async Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        string serverPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with tools for server: {ServerPath}", serverPath);

        try
        {
            // For now, we'll use the MCP client service to handle tool interactions
            // In a more sophisticated implementation, we would integrate MCP tools
            // directly with OpenAI's function calling capabilities

            var toolResponse = await _mcpClientService.ExecuteToolInteractionAsync(
                serverPath, userPrompt, cancellationToken);

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
            throw new LanguageModelException("openai", _config.ModelName, "Tool-based response generation failed", ex);
        }
    }
}

/// <summary>
/// Anthropic language model implementation following LSP
/// Demonstrates Open/Closed Principle - can be added without modifying existing code
/// </summary>
public class AnthropicLanguageModel : ILanguageModel
{
    private readonly AnthropicClient _anthropicClient;
    private readonly ILogger<AnthropicLanguageModel> _logger;
    private readonly LanguageModelConfiguration _config;
    private readonly IMcpClientService _mcpClientService;

    public AnthropicLanguageModel(
        AnthropicClient anthropicClient,
        IOptions<LanguageModelConfiguration> config,
        ILogger<AnthropicLanguageModel> logger,
        IMcpClientService mcpClientService)
    {
        _anthropicClient = anthropicClient;
        _config = config.Value;
        _logger = logger;
        _mcpClientService = mcpClientService;
    }

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with Anthropic model: {ModelName}", _config.ModelName);

        try
        {
            // TODO: Implement proper Anthropic SDK usage
            // For now, return a placeholder until we can verify the correct API
            await Task.Delay(100, cancellationToken); // Simulate API call
            
            var content = "Anthropic implementation pending - correct SDK API needed";
            _logger.LogDebug("Generated response of length: {Length}", content.Length);
            return content;
        }
        catch (Exception ex) when (ex is not LanguageModelException)
        {
            _logger.LogError(ex, "Failed to generate response with Anthropic");
            throw new LanguageModelException("anthropic", _config.ModelName, "Response generation failed", ex);
        }
    }

    public async Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        string serverPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response with tools using Anthropic for server: {ServerPath}", serverPath);

        try
        {
            var toolResponse = await _mcpClientService.ExecuteToolInteractionAsync(
                serverPath, userPrompt, cancellationToken);

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
            _logger.LogError(ex, "Failed to generate response with tools using Anthropic");
            throw new LanguageModelException("anthropic", _config.ModelName, "Tool-based response generation failed", ex);
        }
    }
}