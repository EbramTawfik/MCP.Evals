using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Main evaluation orchestrator interface following DIP
/// </summary>
public interface IEvaluationOrchestrator
{
    /// <summary>
    /// Run a single evaluation
    /// </summary>
    Task<EvaluationResult> RunEvaluationAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run all evaluations in a configuration
    /// </summary>
    Task<IReadOnlyList<EvaluationResult>> RunAllEvaluationsAsync(
        EvaluationConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run evaluations from a configuration file
    /// </summary>
    Task<IReadOnlyList<EvaluationResult>> RunEvaluationsFromFileAsync(
        string configurationFilePath,
        CancellationToken cancellationToken = default);
}