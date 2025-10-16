using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for metrics collection following ISP
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Record evaluation metrics
    /// </summary>
    void RecordEvaluationStarted(string evaluationName);
    void RecordEvaluationCompleted(string evaluationName, TimeSpan duration, EvaluationScore score);
    void RecordEvaluationFailed(string evaluationName, Exception exception);

    /// <summary>
    /// Record MCP client metrics
    /// </summary>
    void RecordMcpConnectionAttempt(string serverPath);
    void RecordMcpConnectionSuccess(string serverPath, TimeSpan duration);
    void RecordMcpConnectionFailure(string serverPath, Exception exception);
}