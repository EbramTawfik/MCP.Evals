using MCP.Evals.Abstractions;
using MCP.Evals.Models;

namespace MCP.Evals.Services;

/// <summary>
/// Detects server types based on file paths and configuration
/// </summary>
public class ServerTypeDetectionService : IServerTypeDetectionService
{
    public ServerType DetectServerType(string serverPath, ServerConfiguration serverConfig)
    {
        if (string.IsNullOrEmpty(serverPath))
        {
            return ServerType.Unknown;
        }

        var extension = Path.GetExtension(serverPath).ToLowerInvariant();
        var pathLower = serverPath.ToLowerInvariant();

        // Check by file extension
        return extension switch
        {
            ".exe" => ServerType.CSharpExecutable,
            ".ts" => ServerType.TypeScriptScript,
            ".js" => ServerType.NodeScript,
            ".py" => ServerType.PythonScript,
            _ => DetectByPathPattern(pathLower)
        };
    }

    private static ServerType DetectByPathPattern(string pathLower)
    {
        // Check by path patterns
        if (pathLower.Contains("typescript") || pathLower.Contains("node"))
        {
            return ServerType.TypeScriptScript;
        }

        if (pathLower.Contains("csharp") || pathLower.Contains("dotnet"))
        {
            return ServerType.CSharpExecutable;
        }

        if (pathLower.Contains("python") || pathLower.Contains("py"))
        {
            return ServerType.PythonScript;
        }

        return ServerType.Unknown;
    }
}