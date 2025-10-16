using MCP.Evals.Abstractions;
using MCP.Evals.Models;

namespace MCP.Evals.Services;

/// <summary>
/// Resolves transport types based on server configuration
/// </summary>
public class TransportResolutionService : ITransportResolutionService
{
    public string ResolveTransportType(ServerConfiguration serverConfig)
    {
        // Explicit transport configuration takes priority
        if (!string.IsNullOrEmpty(serverConfig.Transport))
        {
            return serverConfig.Transport.ToLowerInvariant();
        }

        // Auto-detect based on configuration
        if (!string.IsNullOrEmpty(serverConfig.Url))
        {
            return "http";
        }

        // Default to stdio if we have a path
        if (!string.IsNullOrEmpty(serverConfig.Path))
        {
            return "stdio";
        }

        // Default fallback
        return "stdio";
    }
}