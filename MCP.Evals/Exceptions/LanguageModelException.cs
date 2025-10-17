namespace MCP.Evals.Exceptions;

/// <summary>
/// Exception thrown when language model operations fail
/// </summary>
public class LanguageModelException : McpEvalsException
{
    public string Provider { get; }
    public string ModelName { get; }

    public LanguageModelException(string provider, string modelName, string message)
        : base($"Language model error ({provider}/{modelName}): {message}")
    {
        Provider = provider;
        ModelName = modelName;
    }

    public LanguageModelException(string provider, string modelName, string message, Exception innerException)
        : base($"Language model error ({provider}/{modelName}): {message}", innerException)
    {
        Provider = provider;
        ModelName = modelName;
    }
}