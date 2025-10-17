namespace MCP.Evals.Models;

/// <summary>
/// Configuration for language model
/// </summary>
public sealed class LanguageModelConfiguration
{
    public required string Provider { get; init; } // "openai", "anthropic", "azure-openai"
    public required string Name { get; init; } // Changed from ModelName to Name to match YAML
    public string? ApiKey { get; init; }
    public int MaxTokens { get; init; } = 4000;
    public double Temperature { get; init; } = 0.1;
}