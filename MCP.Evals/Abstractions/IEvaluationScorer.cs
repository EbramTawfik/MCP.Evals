using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for evaluation scoring following SRP
/// </summary>
public interface IEvaluationScorer
{
    /// <summary>
    /// Score a response against a prompt using LLM-based evaluation
    /// </summary>
    Task<EvaluationScore> ScoreResponseAsync(
        string prompt,
        string response,
        string? expectedResult = null,
        CancellationToken cancellationToken = default);
}