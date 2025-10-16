using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for MCP client operations following ISP
/// </summary>
public interface IMcpClientService
{
    /// <summary>
    /// Execute a tool interaction workflow with an MCP server
    /// </summary>
    Task<string> ExecuteToolInteractionAsync(
        ServerConfiguration serverConfig,
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connectivity to an MCP server
    /// </summary>
    Task<bool> TestConnectionAsync(
        ServerConfiguration serverConfig,
        CancellationToken cancellationToken = default);
}