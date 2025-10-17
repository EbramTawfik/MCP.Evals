using Microsoft.Extensions.Logging;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;

namespace MCP.Evals.Configuration;

/// <summary>
/// JSON configuration loader following OCP
/// Can be added without modifying existing code
/// </summary>
public class JsonConfigurationLoader : IConfigurationLoader
{
    private readonly ILogger<JsonConfigurationLoader> _logger;

    public JsonConfigurationLoader(ILogger<JsonConfigurationLoader> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".json";
    }

    public async Task<EvaluationConfiguration> LoadConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading JSON configuration from: {FilePath}", filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            var jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            var configuration = System.Text.Json.JsonSerializer.Deserialize<EvaluationConfiguration>(jsonContent,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (configuration?.Evaluations == null || configuration.Evaluations.Count == 0)
            {
                throw new InvalidOperationException("No evaluations found in configuration");
            }

            _logger.LogInformation("Loaded {EvaluationCount} evaluations from JSON configuration",
                configuration.Evaluations.Count);

            return configuration;
        }
        catch (Exception ex) when (ex is not ConfigurationException)
        {
            _logger.LogError(ex, "Failed to load JSON configuration from: {FilePath}", filePath);
            throw new ConfigurationException(filePath, "Failed to load JSON configuration", ex);
        }
    }
}