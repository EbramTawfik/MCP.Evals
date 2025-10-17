namespace MCP.Evals.Models;

/// <summary>
/// Configuration for MCP server transport
/// </summary>
public sealed class ServerConfiguration
{
    public required string Transport { get; init; } // "stdio", "http"
    public string? Path { get; init; } // Path to executable or script file (required for stdio, optional for http)
    public string? Url { get; init; } // HTTP URL (required for HTTP transport)
    public string[]? Args { get; init; } // Optional command line arguments
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}