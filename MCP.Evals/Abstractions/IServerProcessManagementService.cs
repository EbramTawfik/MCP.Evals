using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for server process management service
/// </summary>
public interface IServerProcessManagementService
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