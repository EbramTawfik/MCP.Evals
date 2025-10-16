using Microsoft.Extensions.Logging;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using System.Diagnostics;

namespace MCP.Evals.Services;

/// <summary>
/// Main evaluation orchestrator following DIP
/// Depends on abstractions, not concrete implementations
/// </summary>
public class EvaluationOrchestrator : IEvaluationOrchestrator
{
    private readonly ILogger<EvaluationOrchestrator> _logger;
    private readonly IMcpClientService _mcpClientService;
    private readonly IEvaluationScorer _evaluationScorer;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IEnumerable<IConfigurationLoader> _configurationLoaders;

    public EvaluationOrchestrator(
        ILogger<EvaluationOrchestrator> logger,
        IMcpClientService mcpClientService,
        IEvaluationScorer evaluationScorer,
        IMetricsCollector metricsCollector,
        IEnumerable<IConfigurationLoader> configurationLoaders)
    {
        _logger = logger;
        _mcpClientService = mcpClientService;
        _evaluationScorer = evaluationScorer;
        _metricsCollector = metricsCollector;
        _configurationLoaders = configurationLoaders;
    }

    public async Task<EvaluationResult> RunEvaluationAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("RunEvaluationAsync with single request is not supported. Use RunEvaluationAsync with EvaluationConfiguration instead.");
    }

    public async Task<EvaluationResult> RunEvaluationAsync(
        EvaluationRequest request,
        ServerConfiguration globalServerConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting evaluation: {EvaluationName}", request.Name);

        var stopwatch = Stopwatch.StartNew();
        _metricsCollector.RecordEvaluationStarted(request.Name);

        try
        {
            // Use the global server config for all evaluations
            var serverConfig = globalServerConfig;

            // Test MCP server connectivity first
            var isConnected = await _mcpClientService.TestConnectionAsync(serverConfig, cancellationToken);
            if (!isConnected)
            {
                throw new McpClientException(serverConfig.Path ?? serverConfig.Url ?? "Unknown", "Unable to connect to MCP server");
            }

            // Execute the tool interaction
            _logger.LogDebug("Executing tool interaction for evaluation: {EvaluationName}", request.Name);
            var response = await _mcpClientService.ExecuteToolInteractionAsync(
                serverConfig,
                request.Prompt,
                cancellationToken);

            // Score the response
            _logger.LogDebug("Scoring response for evaluation: {EvaluationName}", request.Name);
            var score = await _evaluationScorer.ScoreResponseAsync(
                request.Prompt,
                response,
                request.ExpectedResult,
                cancellationToken);

            stopwatch.Stop();

            var result = new EvaluationResult
            {
                Name = request.Name,
                Description = request.Description,
                Prompt = request.Prompt,
                Response = response,
                Score = score,
                Duration = stopwatch.Elapsed
            };

            _metricsCollector.RecordEvaluationCompleted(request.Name, stopwatch.Elapsed, score);
            _logger.LogInformation("Evaluation completed successfully: {EvaluationName} (Score: {AverageScore:F2})",
                request.Name, score.AverageScore);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordEvaluationFailed(request.Name, ex);

            _logger.LogError(ex, "Evaluation failed: {EvaluationName}", request.Name);

            return new EvaluationResult
            {
                Name = request.Name,
                Description = request.Description,
                Prompt = request.Prompt,
                Response = string.Empty,
                Score = new EvaluationScore
                {
                    Accuracy = 1,
                    Completeness = 1,
                    Relevance = 1,
                    Clarity = 1,
                    Reasoning = 1,
                    OverallComments = $"Evaluation failed: {ex.Message}"
                },
                Duration = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<IReadOnlyList<EvaluationResult>> RunAllEvaluationsAsync(
        EvaluationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running {EvaluationCount} evaluations", configuration.Evaluations.Count);

        var results = new List<EvaluationResult>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        var tasks = configuration.Evaluations.Select(async evaluation =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await RunEvaluationAsync(evaluation, configuration.Server, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var evaluationResults = await Task.WhenAll(tasks);
        results.AddRange(evaluationResults);

        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count - successCount;
        var averageScore = results.Where(r => r.IsSuccess).Average(r => r.Score.AverageScore);

        _logger.LogInformation(
            "All evaluations completed. Success: {SuccessCount}, Failed: {FailureCount}, Average Score: {AverageScore:F2}",
            successCount, failureCount, averageScore);

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<EvaluationResult>> RunEvaluationsFromFileAsync(
        string configurationFilePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading evaluation configuration from: {ConfigurationPath}", configurationFilePath);

        // Find appropriate configuration loader using OCP principle
        var loader = _configurationLoaders.FirstOrDefault(l => l.CanHandle(configurationFilePath));
        if (loader == null)
        {
            throw new ConfigurationException(configurationFilePath, "No configuration loader found for this file type");
        }

        var configuration = await loader.LoadConfigurationAsync(configurationFilePath, cancellationToken);
        return await RunAllEvaluationsAsync(configuration, cancellationToken);
    }
}