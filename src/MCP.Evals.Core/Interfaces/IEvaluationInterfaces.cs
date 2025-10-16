namespace MCP.Evals.Core.Interfaces;

/// <summary>
/// Interface for language model abstraction following LSP
/// All implementations must behave consistently
/// </summary>
public interface ILanguageModel
{
    /// <summary>
    /// Generate response using system and user prompts
    /// </summary>
    Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate response with tool interaction capabilities
    /// </summary>
    Task<string> GenerateWithToolsAsync(
        string systemPrompt,
        string userPrompt,
        string serverPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for MCP client operations following ISP
/// </summary>
public interface IMcpClientService
{
    /// <summary>
    /// Execute a tool interaction workflow with an MCP server
    /// </summary>
    Task<string> ExecuteToolInteractionAsync(
        Models.ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connectivity to an MCP server
    /// </summary>
    Task<bool> TestConnectionAsync(
        Models.ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);

    // Legacy overloads for backward compatibility (deprecated)
    [Obsolete("Use overload with ServerConfiguration instead")]
    Task<string> ExecuteToolInteractionAsync(
        string serverPath,
        string prompt,
        CancellationToken cancellationToken = default);

    [Obsolete("Use overload with ServerConfiguration instead")]
    Task<bool> TestConnectionAsync(
        string serverPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for evaluation scoring following SRP
/// </summary>
public interface IEvaluationScorer
{
    /// <summary>
    /// Score a response against a prompt using LLM-based evaluation
    /// </summary>
    Task<Models.EvaluationScore> ScoreResponseAsync(
        string prompt,
        string response,
        string? expectedResult = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for configuration loading following OCP
/// </summary>
public interface IConfigurationLoader
{
    /// <summary>
    /// Load evaluation configuration from file
    /// </summary>
    Task<Models.EvaluationConfiguration> LoadConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine if this loader can handle the given file
    /// </summary>
    bool CanHandle(string filePath);
}

/// <summary>
/// Interface for metrics collection following ISP
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Record evaluation metrics
    /// </summary>
    void RecordEvaluationStarted(string evaluationName);
    void RecordEvaluationCompleted(string evaluationName, TimeSpan duration, Models.EvaluationScore score);
    void RecordEvaluationFailed(string evaluationName, Exception exception);

    /// <summary>
    /// Record MCP client metrics
    /// </summary>
    void RecordMcpConnectionAttempt(string serverPath);
    void RecordMcpConnectionSuccess(string serverPath, TimeSpan duration);
    void RecordMcpConnectionFailure(string serverPath, Exception exception);
}

/// <summary>
/// Main evaluation orchestrator interface following DIP
/// </summary>
public interface IEvaluationOrchestrator
{
    /// <summary>
    /// Run a single evaluation
    /// </summary>
    Task<Models.EvaluationResult> RunEvaluationAsync(
        Models.EvaluationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run all evaluations in a configuration
    /// </summary>
    Task<IReadOnlyList<Models.EvaluationResult>> RunAllEvaluationsAsync(
        Models.EvaluationConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run evaluations from a configuration file
    /// </summary>
    Task<IReadOnlyList<Models.EvaluationResult>> RunEvaluationsFromFileAsync(
        string configurationFilePath,
        CancellationToken cancellationToken = default);
}