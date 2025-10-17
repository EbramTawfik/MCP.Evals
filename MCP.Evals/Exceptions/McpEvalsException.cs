namespace MCP.Evals.Exceptions;

/// <summary>
/// Base exception for MCP Evals operations
/// </summary>
public abstract class McpEvalsException : Exception
{
    protected McpEvalsException(string message) : base(message) { }
    protected McpEvalsException(string message, Exception innerException) : base(message, innerException) { }
}