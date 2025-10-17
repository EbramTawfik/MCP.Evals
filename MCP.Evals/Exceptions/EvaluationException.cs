namespace MCP.Evals.Exceptions;

/// <summary>
/// Exception thrown when evaluation fails
/// </summary>
public class EvaluationException : McpEvalsException
{
    public string EvaluationName { get; }

    public EvaluationException(string evaluationName, string message)
        : base($"Evaluation '{evaluationName}' failed: {message}")
    {
        EvaluationName = evaluationName;
    }

    public EvaluationException(string evaluationName, string message, Exception innerException)
        : base($"Evaluation '{evaluationName}' failed: {message}", innerException)
    {
        EvaluationName = evaluationName;
    }
}