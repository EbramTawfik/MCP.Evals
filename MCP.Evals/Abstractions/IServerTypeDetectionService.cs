using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for server type detection service
/// </summary>
public interface IServerTypeDetectionService
{
    /// <summary>
    /// Detects the server type based on file path and configuration
    /// </summary>
    ServerType DetectServerType(string serverPath, ServerConfiguration serverConfig);
}