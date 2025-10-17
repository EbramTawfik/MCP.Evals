using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for transport resolution service
/// </summary>
public interface ITransportResolutionService
{
    /// <summary>
    /// Determines the appropriate transport type for a server configuration
    /// </summary>
    string ResolveTransportType(ServerConfiguration serverConfig);
}