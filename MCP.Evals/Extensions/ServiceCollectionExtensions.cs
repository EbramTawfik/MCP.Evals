using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Abstractions;
using MCP.Evals.Models;
using MCP.Evals.Configuration;
using MCP.Evals.Services;
using MCP.Evals.Validation;
using MCP.Evals.Metrics;
using OpenAI;
using Anthropic.SDK;
using MCP.Evals.Services.LanguageModels;

namespace MCP.Evals.Extensions;

/// <summary>
/// Dependency injection extensions following DIP
/// High-level modules depend on abstractions, not concretions
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add MCP Evals services to the service collection
    /// </summary>
    public static IServiceCollection AddMcpEvaluations(
        this IServiceCollection services,
        Action<McpEvalsOptions>? configureOptions = null)
    {
        // Configure options
        var options = new McpEvalsOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(Options.Create(options));

        // Add core services following SOLID principles
        AddCoreServices(services);
        AddInfrastructureServices(services, options);
        AddValidationServices(services);
        AddMetricsServices(services, options);

        return services;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        // Main orchestrator (coordinates evaluation workflows)
        services.AddSingleton<IEvaluationOrchestrationService, EvaluationOrchestrationService>();

        // Evaluation scoring (scores responses using language models)
        services.AddSingleton<IEvaluationScoringService, EvaluationScoringService>();

        // MCP connection management (handles connection pooling and reuse)
        services.AddSingleton<IMcpConnectionManager, McpConnectionManager>();

        // MCP client operations (handles MCP protocol communication)
        services.AddSingleton<IMcpClientService, McpClientService>();

        // Transport services (handle different connection types)
        services.AddSingleton<ITransportResolutionService, TransportResolutionService>();
        services.AddSingleton<IServerTypeDetectionService, ServerTypeDetectionService>();
        services.AddSingleton<IServerProcessManagementService, ServerProcessManagementService>();
        services.AddSingleton<IServerProcessManager, ServerProcessManager>();
        services.AddSingleton<ITransportCreationService, TransportCreationService>();
        services.AddSingleton<IToolExecutionPlanningService, ToolExecutionPlanningService>();
    }

    private static void AddInfrastructureServices(IServiceCollection services, McpEvalsOptions options)
    {
        // Language model configuration - use object initialization for init-only properties
        var languageModelConfig = new LanguageModelConfiguration
        {
            Provider = options.DefaultLanguageModel.Provider,
            Name = options.DefaultLanguageModel.Name,
            ApiKey = options.DefaultLanguageModel.ApiKey,
            MaxTokens = options.DefaultLanguageModel.MaxTokens,
            Temperature = options.DefaultLanguageModel.Temperature
        };
        services.AddSingleton(Options.Create(languageModelConfig));

        // HTTP client for Azure OpenAI custom implementation
        services.AddHttpClient();

        // Language model implementations (LSP - all can be substituted)
        services.AddSingleton<ILanguageModel>(provider =>
        {
            var config = provider.GetRequiredService<IOptions<LanguageModelConfiguration>>();
            var logger = provider.GetRequiredService<ILogger<OpenAILanguageService>>();
            var mcpClientService = provider.GetRequiredService<IMcpClientService>();

            // Check for Azure OpenAI first
            var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

            if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureApiKey))
            {
                // Check if verbose mode is enabled
                var isVerbose = bool.TryParse(Environment.GetEnvironmentVariable("MCP_EVALS_VERBOSE"), out var verboseResult) && verboseResult;
                if (isVerbose)
                {
                    Console.WriteLine("[DEBUG] Detected Azure OpenAI configuration - using custom Azure OpenAI client");
                }
                var httpClient = provider.GetRequiredService<HttpClient>();
                var azureLogger = provider.GetRequiredService<ILogger<AzureOpenAILanguageService>>();
                return new AzureOpenAILanguageService(httpClient, config, azureLogger, mcpClientService, azureEndpoint, azureApiKey);
            }

            return config.Value.Provider.ToLower() switch
            {
                "openai" => CreateOpenAILanguageModel(provider, config, logger, mcpClientService),
                "anthropic" => CreateAnthropicLanguageModel(provider, config, mcpClientService),
                _ => throw new InvalidOperationException($"Unsupported language model provider: {config.Value.Provider}")
            };
        });

        // Configuration loaders (OCP - can add new types without modifying existing code)
        services.AddSingleton<IConfigurationLoader, YamlConfigurationLoader>();
        services.AddSingleton<IConfigurationLoader, JsonConfigurationLoader>();
    }

    private static void AddValidationServices(IServiceCollection services)
    {
        // FluentValidation (ISP - specific validation interfaces)
        services.AddSingleton<IValidator<EvaluationRequest>, EvaluationRequestValidator>();
        services.AddSingleton<IValidator<EvaluationConfiguration>, EvaluationConfigurationValidator>();
        services.AddSingleton<IValidator<LanguageModelConfiguration>, LanguageModelConfigurationValidator>();
    }

    private static void AddMetricsServices(IServiceCollection services, McpEvalsOptions options)
    {
        // Metrics collectors (ISP - only metrics-related functionality)
        if (options.EnablePrometheusMetrics)
        {
            services.AddSingleton<IMetricsCollector, PrometheusMetricsCollector>();
        }
        else
        {
            services.AddSingleton<IMetricsCollector, ConsoleMetricsCollector>();
        }
    }

    private static OpenAILanguageService CreateOpenAILanguageModel(
        IServiceProvider provider,
        IOptions<LanguageModelConfiguration> config,
        ILogger<OpenAILanguageService> logger,
        IMcpClientService mcpClientService)
    {
        Console.WriteLine($"[DEBUG] Creating OpenAI Language Model...");
        Console.WriteLine($"[DEBUG] Config API Key: {(string.IsNullOrEmpty(config.Value.ApiKey) ? "NOT SET" : "SET")}");

        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var azureApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

        Console.WriteLine($"[DEBUG] Environment API Key: {(string.IsNullOrEmpty(envApiKey) ? "NOT SET" : "SET")}");
        Console.WriteLine($"[DEBUG] Azure API Key: {(string.IsNullOrEmpty(azureApiKey) ? "NOT SET" : "SET")}");
        Console.WriteLine($"[DEBUG] Azure Endpoint: {(string.IsNullOrEmpty(azureEndpoint) ? "NOT SET" : "SET")}");

        var apiKey = config.Value.ApiKey ?? azureApiKey ?? envApiKey;
        Console.WriteLine($"[DEBUG] Final API Key: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : "SET")}");

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key not configured. Set OPENAI_API_KEY or AZURE_OPENAI_API_KEY environment variable or configure in options.");
        }

        OpenAIClient openAIClient;

        // Check if we're using Azure OpenAI (has endpoint) or regular OpenAI
        if (!string.IsNullOrEmpty(azureEndpoint))
        {
            Console.WriteLine($"[DEBUG] Using Azure OpenAI with endpoint: {azureEndpoint}");
            var credential = new System.ClientModel.ApiKeyCredential(apiKey);
            openAIClient = new OpenAIClient(credential, new OpenAIClientOptions
            {
                Endpoint = new Uri(azureEndpoint)
            });
        }
        else
        {
            Console.WriteLine($"[DEBUG] Using OpenAI API");
            openAIClient = new OpenAIClient(apiKey);
        }
        return new OpenAILanguageService(openAIClient, config, logger, mcpClientService);
    }

    private static AnthropicLanguageService CreateAnthropicLanguageModel(
        IServiceProvider provider,
        IOptions<LanguageModelConfiguration> config,
        IMcpClientService mcpClientService)
    {
        var apiKey = config.Value.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Anthropic API key not configured. Set ANTHROPIC_API_KEY environment variable or configure in options.");
        }

        var logger = provider.GetRequiredService<ILogger<AnthropicLanguageService>>();
        return new AnthropicLanguageService(config, logger, mcpClientService);
    }
}