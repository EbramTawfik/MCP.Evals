using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;

namespace MCP.Evals.Metrics;

/// <summary>
/// Console metrics collector for development/testing following ISP
/// Demonstrates Open/Closed Principle - can be added without modifying existing code
/// </summary>
public class ConsoleMetricsCollector : IMetricsCollector
{
    private readonly ILogger<ConsoleMetricsCollector> _logger;

    public ConsoleMetricsCollector(ILogger<ConsoleMetricsCollector> logger)
    {
        _logger = logger;
    }

    public void RecordEvaluationStarted(string evaluationName)
    {
        _logger.LogInformation("📊 METRIC: Evaluation started - {EvaluationName}", evaluationName);
    }

    public void RecordEvaluationCompleted(string evaluationName, TimeSpan duration, EvaluationScore score)
    {
        _logger.LogInformation("📊 METRIC: Evaluation completed - {EvaluationName} | Duration: {Duration}ms | Score: {Score:F2}/5.0",
            evaluationName, duration.TotalMilliseconds, score.AverageScore);
    }

    public void RecordEvaluationFailed(string evaluationName, Exception exception)
    {
        _logger.LogWarning("📊 METRIC: Evaluation failed - {EvaluationName} | Error: {ErrorType}",
            evaluationName, exception.GetType().Name);
    }

    public void RecordMcpConnectionAttempt(string serverPath)
    {
        _logger.LogInformation("📊 METRIC: MCP connection attempt - {ServerPath}", Path.GetFileName(serverPath));
    }

    public void RecordMcpConnectionSuccess(string serverPath, TimeSpan duration)
    {
        _logger.LogInformation("📊 METRIC: MCP connection success - {ServerPath} | Duration: {Duration}ms",
            Path.GetFileName(serverPath), duration.TotalMilliseconds);
    }

    public void RecordMcpConnectionFailure(string serverPath, Exception exception)
    {
        _logger.LogWarning("📊 METRIC: MCP connection failure - {ServerPath} | Error: {ErrorType}",
            Path.GetFileName(serverPath), exception.GetType().Name);
    }
}