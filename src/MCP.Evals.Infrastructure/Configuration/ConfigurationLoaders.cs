using Microsoft.Extensions.Logging;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCP.Evals.Infrastructure.Configuration;

/// <summary>
/// YAML configuration loader following OCP
/// Can handle YAML files without modifying existing code
/// </summary>
public class YamlConfigurationLoader : IConfigurationLoader
{
    private readonly ILogger<YamlConfigurationLoader> _logger;
    private readonly IDeserializer _yamlDeserializer;

    public YamlConfigurationLoader(ILogger<YamlConfigurationLoader> logger)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public bool CanHandle(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".yaml" || extension == ".yml";
    }

    public async Task<EvaluationConfiguration> LoadConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading YAML configuration from: {FilePath}", filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            var yamlContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            var yamlConfig = _yamlDeserializer.Deserialize<YamlEvaluationConfig>(yamlContent);

            if (yamlConfig?.Evals == null || yamlConfig.Evals.Count == 0)
            {
                throw new InvalidOperationException("No evaluations found in configuration");
            }

            var configuration = ConvertToEvaluationConfiguration(yamlConfig, filePath);

            _logger.LogInformation("Loaded {EvaluationCount} evaluations from YAML configuration",
                configuration.Evaluations.Count);

            return configuration;
        }
        catch (Exception ex) when (ex is not ConfigurationException)
        {
            _logger.LogError(ex, "Failed to load YAML configuration from: {FilePath}", filePath);
            throw new ConfigurationException(filePath, "Failed to load YAML configuration", ex);
        }
    }

    private static EvaluationConfiguration ConvertToEvaluationConfiguration(
        YamlEvaluationConfig yamlConfig,
        string filePath)
    {
        // Convert model configuration
        var modelConfig = yamlConfig.Model != null
            ? new LanguageModelConfiguration
            {
                Provider = yamlConfig.Model.Provider ?? "openai",
                ModelName = yamlConfig.Model.Name ?? "gpt-4o",
                ApiKey = yamlConfig.Model.ApiKey,
                MaxTokens = yamlConfig.Model.MaxTokens ?? 4000,
                Temperature = yamlConfig.Model.Temperature ?? 0.1
            }
            : new LanguageModelConfiguration
            {
                Provider = "openai",
                ModelName = "gpt-4o"
            };

        // Convert evaluations
        var evaluations = yamlConfig.Evals.Select(eval => new EvaluationRequest
        {
            Name = eval.Name ?? "Unnamed Evaluation",
            Description = eval.Description ?? "No description provided",
            Prompt = eval.Prompt ?? throw new InvalidOperationException("Prompt is required"),
            ServerPath = DetermineServerPath(eval, filePath),
            ExpectedResult = eval.ExpectedResult
        }).ToList();

        return new EvaluationConfiguration
        {
            Model = modelConfig,
            Evaluations = evaluations,
            Name = yamlConfig.Name,
            Description = yamlConfig.Description
        };
    }

    private static string DetermineServerPath(YamlEvaluation eval, string configFilePath)
    {
        // If server_path is specified in the evaluation, use it
        if (!string.IsNullOrEmpty(eval.ServerPath))
        {
            return Path.IsPathFullyQualified(eval.ServerPath)
                ? eval.ServerPath
                : Path.Combine(Path.GetDirectoryName(configFilePath)!, eval.ServerPath);
        }

        // Otherwise, look for a server file in the same directory as the config
        var configDir = Path.GetDirectoryName(configFilePath)!;
        var possibleServerFiles = new[] { "index.ts", "index.js", "server.ts", "server.js", "main.py" };

        foreach (var serverFile in possibleServerFiles)
        {
            var serverPath = Path.Combine(configDir, serverFile);
            if (File.Exists(serverPath))
            {
                return serverPath;
            }
        }

        throw new ConfigurationException(configFilePath,
            "No server path specified and no default server file found");
    }    // YAML data models
    private class YamlEvaluationConfig
    {
        public YamlModelConfig? Model { get; set; }
        public List<YamlEvaluation> Evals { get; set; } = new();
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    private class YamlModelConfig
    {
        public string? Provider { get; set; }
        public string? Name { get; set; }
        public string? ApiKey { get; set; }
        public int? MaxTokens { get; set; }
        public double? Temperature { get; set; }
    }

    private class YamlEvaluation
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Prompt { get; set; }
        public string? ServerPath { get; set; }
        public string? ExpectedResult { get; set; }
    }
}

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