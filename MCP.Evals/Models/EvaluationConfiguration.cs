namespace MCP.Evals.Models;

/// <summary>
/// Configuration for a complete evaluation suite
/// </summary>
public sealed class EvaluationConfiguration
{
    public required LanguageModelConfiguration Model { get; init; }
    public required ServerConfiguration Server { get; init; } // Added server configuration
    public required IReadOnlyList<EvaluationRequest> Evaluations { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}