using Microsoft.Extensions.Logging;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using Prometheus;
using System.Diagnostics;

namespace MCP.Evals.Infrastructure.Metrics;

/// <summary>
/// Prometheus metrics collector following ISP
/// Only implements metrics-related functionality
/// </summary>
public class PrometheusMetricsCollector : IMetricsCollector
{
    private readonly ILogger<PrometheusMetricsCollector> _logger;

    // Prometheus metrics
    private static readonly Counter EvaluationsStarted = Prometheus.Metrics
        .CreateCounter("mcp_evaluations_started_total", "Total number of evaluations started", new[] { "evaluation_name" });

    private static readonly Counter EvaluationsCompleted = Prometheus.Metrics
        .CreateCounter("mcp_evaluations_completed_total", "Total number of evaluations completed", new[] { "evaluation_name" });

    private static readonly Counter EvaluationsFailed = Prometheus.Metrics
        .CreateCounter("mcp_evaluations_failed_total", "Total number of evaluations failed", new[] { "evaluation_name", "error_type" });

    private static readonly Histogram EvaluationDuration = Prometheus.Metrics
        .CreateHistogram("mcp_evaluation_duration_seconds", "Duration of evaluations in seconds", new[] { "evaluation_name" });

    private static readonly Histogram EvaluationScores = Prometheus.Metrics
        .CreateHistogram("mcp_evaluation_scores", "Evaluation scores", new[] { "evaluation_name", "metric_type" });

    private static readonly Counter McpConnectionAttempts = Prometheus.Metrics
        .CreateCounter("mcp_connection_attempts_total", "Total MCP connection attempts", new[] { "server_path" });

    private static readonly Counter McpConnectionSuccesses = Prometheus.Metrics
        .CreateCounter("mcp_connection_successes_total", "Total successful MCP connections", new[] { "server_path" });

    private static readonly Counter McpConnectionFailures = Prometheus.Metrics
        .CreateCounter("mcp_connection_failures_total", "Total failed MCP connections", new[] { "server_path", "error_type" });

    private static readonly Histogram McpConnectionDuration = Prometheus.Metrics
        .CreateHistogram("mcp_connection_duration_seconds", "Duration of MCP connections in seconds", new[] { "server_path" });

    public PrometheusMetricsCollector(ILogger<PrometheusMetricsCollector> logger)
    {
        _logger = logger;
    }

    public void RecordEvaluationStarted(string evaluationName)
    {
        EvaluationsStarted.WithLabels(evaluationName).Inc();
        _logger.LogDebug("Recorded evaluation started: {EvaluationName}", evaluationName);
    }

    public void RecordEvaluationCompleted(string evaluationName, TimeSpan duration, EvaluationScore score)
    {
        EvaluationsCompleted.WithLabels(evaluationName).Inc();
        EvaluationDuration.WithLabels(evaluationName).Observe(duration.TotalSeconds);

        // Record individual score metrics
        EvaluationScores.WithLabels(evaluationName, "accuracy").Observe(score.Accuracy);
        EvaluationScores.WithLabels(evaluationName, "completeness").Observe(score.Completeness);
        EvaluationScores.WithLabels(evaluationName, "relevance").Observe(score.Relevance);
        EvaluationScores.WithLabels(evaluationName, "clarity").Observe(score.Clarity);
        EvaluationScores.WithLabels(evaluationName, "reasoning").Observe(score.Reasoning);
        EvaluationScores.WithLabels(evaluationName, "average").Observe(score.AverageScore);

        _logger.LogDebug("Recorded evaluation completed: {EvaluationName}, Duration: {Duration}ms, Score: {Score:F2}",
            evaluationName, duration.TotalMilliseconds, score.AverageScore);
    }

    public void RecordEvaluationFailed(string evaluationName, Exception exception)
    {
        var errorType = exception.GetType().Name;
        EvaluationsFailed.WithLabels(evaluationName, errorType).Inc();

        _logger.LogDebug("Recorded evaluation failed: {EvaluationName}, Error: {ErrorType}",
            evaluationName, errorType);
    }

    public void RecordMcpConnectionAttempt(string serverPath)
    {
        var serverName = GetServerName(serverPath);
        McpConnectionAttempts.WithLabels(serverName).Inc();

        _logger.LogDebug("Recorded MCP connection attempt: {ServerPath}", serverPath);
    }

    public void RecordMcpConnectionSuccess(string serverPath, TimeSpan duration)
    {
        var serverName = GetServerName(serverPath);
        McpConnectionSuccesses.WithLabels(serverName).Inc();
        McpConnectionDuration.WithLabels(serverName).Observe(duration.TotalSeconds);

        _logger.LogDebug("Recorded MCP connection success: {ServerPath}, Duration: {Duration}ms",
            serverPath, duration.TotalMilliseconds);
    }

    public void RecordMcpConnectionFailure(string serverPath, Exception exception)
    {
        var serverName = GetServerName(serverPath);
        var errorType = exception.GetType().Name;
        McpConnectionFailures.WithLabels(serverName, errorType).Inc();

        _logger.LogDebug("Recorded MCP connection failure: {ServerPath}, Error: {ErrorType}",
            serverPath, errorType);
    }

    private static string GetServerName(string serverPath)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(serverPath);
        }
        catch
        {
            return "unknown";
        }
    }
}

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