using MCP.Evals.Abstractions;
using MCP.Evals.Models;

namespace MCP.Evals.Services;

/// <summary>
/// Service for resolving transport types based on server configuration
/// Follows SRP - only responsible for transport type resolution
/// </summary>
public class TransportResolver : ITransportResolver
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