using MCP.Evals.Models;
using ModelContextProtocol.Client;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Manages MCP client connections per configuration to avoid recreation overhead
/// </summary>
public interface IMcpConnectionManager : IDisposable
{
    /// <summary>
    /// Gets or creates a reusable MCP client for the given server configuration
    /// </summary>
    Task<McpClient> GetOrCreateClientAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to an MCP server using a cached or new connection
    /// </summary>
    Task<bool> TestConnectionAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes all cached connections and cleans up resources
    /// </summary>
    Task CloseAllConnectionsAsync();
}