namespace MCP.Evals.Models;

/// <summary>
/// Request for running an evaluation
/// </summary>
public sealed class EvaluationRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public string? ExpectedResult { get; init; }
}