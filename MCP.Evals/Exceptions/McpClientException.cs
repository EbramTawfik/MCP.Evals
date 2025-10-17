namespace MCP.Evals.Exceptions;

/// <summary>
/// Exception thrown when MCP client operations fail
/// </summary>
public class McpClientException : McpEvalsException
{
    public string ServerPath { get; }

    public McpClientException(string serverPath, string message)
        : base($"MCP client error for server '{serverPath}': {message}")
    {
        ServerPath = serverPath;
    }

    public McpClientException(string serverPath, string message, Exception innerException)
        : base($"MCP client error for server '{serverPath}': {message}", innerException)
    {
        ServerPath = serverPath;
    }
}