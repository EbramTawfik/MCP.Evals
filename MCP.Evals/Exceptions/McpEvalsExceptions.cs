namespace MCP.Evals.Exceptions;

/// <summary>
/// Base exception for MCP Evals operations
/// </summary>
public abstract class McpEvalsException : Exception
{
    protected McpEvalsException(string message) : base(message) { }
    protected McpEvalsException(string message, Exception innerException) : base(message, innerException) { }
}

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

/// <summary>
/// Exception thrown when configuration is invalid
/// </summary>
public class ConfigurationException : McpEvalsException
{
    public string ConfigurationPath { get; }

    public ConfigurationException(string configurationPath, string message)
        : base($"Configuration error in '{configurationPath}': {message}")
    {
        ConfigurationPath = configurationPath;
    }

    public ConfigurationException(string configurationPath, string message, Exception innerException)
        : base($"Configuration error in '{configurationPath}': {message}", innerException)
    {
        ConfigurationPath = configurationPath;
    }
}

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