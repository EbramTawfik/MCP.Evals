using Microsoft.Extensions.Logging;
using MCP.Evals.Exceptions;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCP.Evals.Configuration;

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
                Name = yamlConfig.Model.Name ?? "gpt-4o",
                ApiKey = yamlConfig.Model.ApiKey,
                MaxTokens = yamlConfig.Model.MaxTokens ?? 4000,
                Temperature = yamlConfig.Model.Temperature ?? 0.1
            }
            : new LanguageModelConfiguration
            {
                Provider = "openai",
                Name = "gpt-4o"
            };

        // Convert server configuration
        var serverConfig = yamlConfig.Server != null
            ? new ServerConfiguration
            {
                Transport = yamlConfig.Server.Transport ?? "stdio",
                Path = yamlConfig.Server.Path != null ? ResolvePath(yamlConfig.Server.Path, filePath) : null,
                Url = yamlConfig.Server.Url,
                Args = yamlConfig.Server.Args,
            }
            : throw new InvalidOperationException("Server configuration is required");

        // Convert evaluations
        var evaluations = yamlConfig.Evals.Select(eval => new EvaluationRequest
        {
            Name = eval.Name ?? "Unnamed Evaluation",
            Description = eval.Description ?? "No description provided",
            Prompt = eval.Prompt ?? throw new InvalidOperationException("Prompt is required"),
            ExpectedResult = eval.ExpectedResult,
        }).ToList();

        return new EvaluationConfiguration
        {
            Model = modelConfig,
            Server = serverConfig,
            Evaluations = evaluations,
            Name = yamlConfig.Name,
            Description = yamlConfig.Description
        };
    }

    private static string ResolvePath(string path, string configFilePath)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        // Resolve relative path based on config file location
        var configDir = Path.GetDirectoryName(configFilePath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(configDir, path));
    }

    // YAML data models
    private class YamlEvaluationConfig
    {
        public YamlModelConfig? Model { get; set; }
        public YamlServerConfig? Server { get; set; }
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

    private class YamlServerConfig
    {
        public string? Transport { get; set; }
        public string? Path { get; set; }
        public string? Url { get; set; }
        public string[]? Args { get; set; }
        public double? Timeout { get; set; }
    }

    private class YamlEvaluation
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Prompt { get; set; }
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