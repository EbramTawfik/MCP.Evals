using MCP.Evals.Models;

namespace MCP.Evals.Abstractions;

/// <summary>
/// Interface for configuration loading following OCP
/// </summary>
public interface IConfigurationLoader
{
    /// <summary>
    /// Load evaluation configuration from file
    /// </summary>
    Task<EvaluationConfiguration> LoadConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine if this loader can handle the given file
    /// </summary>
    bool CanHandle(string filePath);
}