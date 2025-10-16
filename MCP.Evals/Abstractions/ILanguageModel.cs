using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for language model abstraction following LSP
/// All implementations must behave consistently
/// </summary>
public interface ILanguageModel
{
    /// <summary>
    /// Generate response using system and user prompts
    /// </summary>
    Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate response with tool interaction capabilities
    /// </summary>
    Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);
}