using MCP.Evals.Models;
using System.Diagnostics;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Manages server processes to ensure they are reused per configuration rather than recreated
/// </summary>
public interface IServerProcessManager : IDisposable
{
    /// <summary>
    /// Gets or starts a server process for the given configuration
    /// Reuses existing processes when possible
    /// </summary>
    Task<Process?> GetOrStartServerProcessAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all running server processes and cleans up resources
    /// </summary>
    Task StopAllServerProcessesAsync();

    /// <summary>
    /// Checks if a server process is needed for the given configuration
    /// </summary>
    bool IsServerProcessRequired(ServerConfiguration serverConfig);
}