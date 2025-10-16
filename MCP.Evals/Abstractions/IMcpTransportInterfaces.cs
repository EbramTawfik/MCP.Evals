using MCP.Evals.Models;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for determining transport type based on server configuration
/// </summary>
public interface ITransportResolver
{
    /// <summary>
    /// Determines the appropriate transport type for a server configuration
    /// </summary>
    string ResolveTransportType(ServerConfiguration serverConfig);
}

/// <summary>
/// Interface for creating MCP client transports
/// </summary>
public interface ITransportFactory
{
    /// <summary>
    /// Creates a client transport for the specified type and configuration
    /// </summary>
    Task<IClientTransport> CreateTransportAsync(
        string transportType,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for detecting server file types
/// </summary>
public interface IServerTypeDetector
{
    /// <summary>
    /// Detects the server type based on file path and configuration
    /// </summary>
    ServerType DetectServerType(string serverPath, ServerConfiguration serverConfig);
}

/// <summary>
/// Interface for starting server processes based on server type
/// </summary>
public interface IServerProcessManager
{
    /// <summary>
    /// Starts a server process based on the detected server type
    /// </summary>
    Task<System.Diagnostics.Process> StartServerAsync(
        ServerType serverType,
        string serverPath,
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a server is ready at the specified endpoint
    /// </summary>
    Task<bool> IsServerReadyAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for managing tool execution decisions
/// </summary>
public interface IToolExecutionPlanner
{
    /// <summary>
    /// Determines which tools to execute based on a prompt and available tools
    /// </summary>
    Task<IReadOnlyList<ToolExecution>> PlanToolExecutionsAsync(
        string prompt,
        IList<ModelContextProtocol.Client.McpClientTool> availableTools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tool interaction workflow with planning and execution
    /// </summary>
    Task<string> ExecuteToolInteractionAsync(
        ModelContextProtocol.Client.McpClient client,
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Enumeration of supported server types
/// </summary>
public enum ServerType
{
    NodeScript,
    TypeScriptScript,
    CSharpExecutable,
    PythonScript,
    Unknown
}

/// <summary>
/// Represents a tool execution plan
/// </summary>
public sealed class ToolExecution
{
    public required string ToolName { get; init; }
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }
}