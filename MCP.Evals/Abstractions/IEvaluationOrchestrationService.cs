using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for evaluation orchestration service
/// </summary>
public interface IEvaluationOrchestrationService
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