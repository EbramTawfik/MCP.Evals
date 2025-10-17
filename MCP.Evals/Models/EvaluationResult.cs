using Ardalis.GuardClauses;

namespace MCP.Evals.Models;

/// <summary>
/// Represents the result of an evaluation with detailed scoring metrics
/// </summary>
public sealed class EvaluationResult
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required string Response { get; init; }
    public required EvaluationScore Score { get; init; }
    public required TimeSpan Duration { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? ErrorMessage { get; init; }
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}