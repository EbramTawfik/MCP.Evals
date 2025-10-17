using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using Prometheus;
using System.Diagnostics;

namespace MCP.Evals.Metrics;

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
        var serverName = GetServerName(evaluationName);
        EvaluationsStarted.WithLabels(serverName).Inc();

        _logger.LogDebug("Recorded evaluation started: {EvaluationName}", evaluationName);
    }

    public void RecordEvaluationCompleted(string evaluationName, TimeSpan duration, EvaluationScore score)
    {
        var serverName = GetServerName(evaluationName);
        EvaluationsCompleted.WithLabels(serverName).Inc();
        EvaluationDuration.WithLabels(serverName).Observe(duration.TotalSeconds);

        // Record individual score metrics
        EvaluationScores.WithLabels(serverName, "accuracy").Observe(score.Accuracy);
        EvaluationScores.WithLabels(serverName, "completeness").Observe(score.Completeness);
        EvaluationScores.WithLabels(serverName, "relevance").Observe(score.Relevance);
        EvaluationScores.WithLabels(serverName, "clarity").Observe(score.Clarity);
        EvaluationScores.WithLabels(serverName, "reasoning").Observe(score.Reasoning);

        _logger.LogDebug("Recorded evaluation completed: {EvaluationName}, Duration: {Duration}ms, Score: {Score:F2}",
            evaluationName, duration.TotalMilliseconds, score.AverageScore);
    }

    public void RecordEvaluationFailed(string evaluationName, Exception exception)
    {
        var serverName = GetServerName(evaluationName);
        var errorType = exception.GetType().Name;
        EvaluationsFailed.WithLabels(serverName, errorType).Inc();

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