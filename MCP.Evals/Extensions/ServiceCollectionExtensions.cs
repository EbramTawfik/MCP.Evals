using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCP.Evals.Abstractions;
using MCP.Evals.Commands;
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
            // Removed IMcpClientService dependency to fix circular dependency
            // Get command options 
            var commandOptions = provider.GetService<EvaluationCommandOptions>();
            var isVerbose = commandOptions?.Verbose ?? false;

            // Use the provider specified in the YAML configuration to determine how to create the language model
            return config.Value.Provider.ToLower() switch
            {
                "azure-openai" => CreateAzureOpenAILanguageModel(provider, config, logger, commandOptions),
                "openai" => CreateOpenAILanguageModel(provider, config, logger),
                "anthropic" => CreateAnthropicLanguageModel(provider, config),
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
        ILogger<OpenAILanguageService> logger)
    {
        Console.WriteLine($"[DEBUG] Creating OpenAI Language Model...");
        Console.WriteLine($"[DEBUG] Config API Key: {(string.IsNullOrEmpty(config.Value.ApiKey) ? "NOT SET" : "SET")}");

        // Try to get command options first, fallback to environment variables for backwards compatibility
        var commandOptions = provider.GetService<EvaluationCommandOptions>();

        var apiKeyFromCommand = commandOptions?.ApiKey;
        var endpoint = commandOptions?.Endpoint;

        Console.WriteLine($"[DEBUG] Command API Key: {(string.IsNullOrEmpty(apiKeyFromCommand) ? "NOT SET" : "SET")}");
        Console.WriteLine($"[DEBUG] Endpoint: {(string.IsNullOrEmpty(endpoint) ? "NOT SET" : "SET")}");

        var apiKey = config.Value.ApiKey ?? apiKeyFromCommand;
        Console.WriteLine($"[DEBUG] Final API Key: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : "SET")}");

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("API key not configured. Provide --api-key command line argument or configure in YAML configuration.");
        }

        OpenAIClient openAIClient;

        // Check if we're using a custom endpoint or regular OpenAI
        if (!string.IsNullOrEmpty(endpoint))
        {
            Console.WriteLine($"[DEBUG] Using custom endpoint: {endpoint}");
            var credential = new System.ClientModel.ApiKeyCredential(apiKey);
            openAIClient = new OpenAIClient(credential, new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            });
        }
        else
        {
            Console.WriteLine($"[DEBUG] Using OpenAI API");
            openAIClient = new OpenAIClient(apiKey);
        }
        return new OpenAILanguageService(openAIClient, config, logger);
    }

    private static AzureOpenAILanguageService CreateAzureOpenAILanguageModel(
        IServiceProvider provider,
        IOptions<LanguageModelConfiguration> config,
        ILogger<OpenAILanguageService> logger,
        EvaluationCommandOptions? commandOptions)
    {
        var isVerbose = commandOptions?.Verbose ?? false;

        if (isVerbose)
        {
            Console.WriteLine($"[DEBUG] Creating Azure OpenAI Language Model...");
        }

        // Use API key from config or command line
        var apiKey = config.Value.ApiKey ?? commandOptions?.ApiKey;

        // Use endpoint from command line (required for Azure OpenAI)
        var endpoint = commandOptions?.Endpoint;

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("API key not configured. Provide --api-key command line argument or configure in YAML.");
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("Azure OpenAI endpoint not configured. Provide --endpoint command line argument.");
        }

        if (isVerbose)
        {
            Console.WriteLine($"[DEBUG] Using Azure OpenAI with endpoint: {endpoint}");
        }

        var httpClient = provider.GetRequiredService<HttpClient>();
        var azureLogger = provider.GetRequiredService<ILogger<AzureOpenAILanguageService>>();
        return new AzureOpenAILanguageService(httpClient, config, azureLogger, endpoint, apiKey);
    }

    private static AnthropicLanguageService CreateAnthropicLanguageModel(
        IServiceProvider provider,
        IOptions<LanguageModelConfiguration> config)
    {
        var commandOptions = provider.GetService<EvaluationCommandOptions>();
        var apiKey = config.Value.ApiKey ?? commandOptions?.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Anthropic API key not configured. Provide --api-key command line argument or configure in YAML configuration.");
        }

        var logger = provider.GetRequiredService<ILogger<AnthropicLanguageService>>();
        return new AnthropicLanguageService(config, logger);
    }
}