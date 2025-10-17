namespace MCP.Evals.Models;

/// <summary>
/// Represents a tool execution plan
/// </summary>
public sealed class ToolExecution
{
    public required string ToolName { get; init; }
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }
}