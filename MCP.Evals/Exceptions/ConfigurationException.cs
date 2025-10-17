namespace MCP.Evals.Exceptions;

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