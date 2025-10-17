using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Extensions;
using MCP.Evals.Models;
using System.CommandLine;

namespace MCP.Evals.Commands;

/// <summary>
/// Validate command following SRP - only responsible for validation operations
/// </summary>
public class ValidateCommand : Command
{
    public ValidateCommand() : base("validate", "Validate an MCP evaluation configuration file")
    {
        var configPathArgument = new Argument<string>(
            "config-path",
            "Path to the evaluation configuration file to validate");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose output");

        var checkConnectivityOption = new Option<bool>(
            ["--check-connectivity", "-c"],
            "Test connectivity to MCP servers specified in the configuration");

        AddArgument(configPathArgument);
        AddOption(verboseOption);
        AddOption(checkConnectivityOption);

        this.SetHandler(ExecuteAsync, configPathArgument, verboseOption, checkConnectivityOption);
    }

    private static async Task<int> ExecuteAsync(string configPath, bool verbose, bool checkConnectivity)
    {
        try
        {
            // Build and configure the host with command-line options
            var hostBuilder = CreateHostBuilderWithOptions(verbose);

            // Remove the environment variable setting
            // Environment.SetEnvironmentVariable("MCP_EVALS_VERBOSE", verbose.ToString());

            if (verbose)
            {
                hostBuilder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                });
            }

            using var host = hostBuilder.Build();

            var logger = host.Services.GetRequiredService<ILogger<ValidateCommand>>();
            var configurationLoaders = host.Services.GetRequiredService<IEnumerable<IConfigurationLoader>>();
            var validators = host.Services.GetRequiredService<IValidator<EvaluationConfiguration>>();
            var mcpClientService = host.Services.GetRequiredService<IMcpClientService>();

            logger.LogInformation("Validating configuration file: {ConfigPath}", configPath);

            // Check if file exists
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"❌ Configuration file not found: {configPath}");
                return 1;
            }

            // Find appropriate loader
            var loader = configurationLoaders.FirstOrDefault(l => l.CanHandle(configPath));
            if (loader == null)
            {
                Console.Error.WriteLine($"❌ No configuration loader found for file type: {Path.GetExtension(configPath)}");
                Console.Error.WriteLine("Supported formats: .yaml, .yml, .json");
                return 1;
            }

            Console.WriteLine($"✅ Found configuration loader for {Path.GetExtension(configPath)} files");

            // Load configuration
            EvaluationConfiguration configuration;
            try
            {
                configuration = await loader.LoadConfigurationAsync(configPath);
                Console.WriteLine("✅ Configuration file loaded successfully");

                if (verbose)
                {
                    Console.WriteLine($"   - Found {configuration.Evaluations.Count} evaluations");
                    Console.WriteLine($"   - Language model: {configuration.Model.Provider}/{configuration.Model.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to load configuration: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine($"   Stack trace: {ex.StackTrace}");
                }
                return 1;
            }

            // Validate configuration
            var validationResult = await validators.ValidateAsync(configuration);
            if (!validationResult.IsValid)
            {
                Console.Error.WriteLine("❌ Configuration validation failed:");
                foreach (var error in validationResult.Errors)
                {
                    Console.Error.WriteLine($"   - {error.PropertyName}: {error.ErrorMessage}");
                }
                return 1;
            }

            Console.WriteLine("✅ Configuration validation passed");

            // Validate individual evaluations
            var evaluationValidator = host.Services.GetRequiredService<IValidator<EvaluationRequest>>();
            var evaluationErrors = new List<string>();

            foreach (var evaluation in configuration.Evaluations)
            {
                var evalValidation = await evaluationValidator.ValidateAsync(evaluation);
                if (!evalValidation.IsValid)
                {
                    evaluationErrors.AddRange(evalValidation.Errors.Select(e =>
                        $"Evaluation '{evaluation.Name}' - {e.PropertyName}: {e.ErrorMessage}"));
                }
            }

            if (evaluationErrors.Any())
            {
                Console.Error.WriteLine("❌ Evaluation validation failed:");
                foreach (var error in evaluationErrors)
                {
                    Console.Error.WriteLine($"   - {error}");
                }
                return 1;
            }

            Console.WriteLine("✅ All evaluations are valid");

            // Check server file existence
            var missingServers = new List<string>();

            // Check global server configuration
            if (!IsValidServerPath(configuration.Server))
            {
                missingServers.Add($"Global server: {GetServerPathError(configuration.Server)}");
            }

            // Server validation is complete since all evaluations use the global server
            if (missingServers.Any())
            {
                Console.Error.WriteLine("❌ Server file validation failed:");
                foreach (var error in missingServers)
                {
                    Console.Error.WriteLine($"   - {error}");
                }
                return 1;
            }

            Console.WriteLine("✅ All server files exist");

            // Test connectivity if requested
            if (checkConnectivity)
            {
                Console.WriteLine("\n🔍 Testing MCP server connectivity...");

                var connectivityResults = new List<(string Name, string ServerPath, bool IsConnected, string? Error)>();

                foreach (var evaluation in configuration.Evaluations)
                {
                    if (verbose)
                    {
                        Console.WriteLine($"   Testing connection to {evaluation.Name}...");
                    }

                    try
                    {
                        // Use global server config for all evaluations
                        var serverConfig = configuration.Server;
                        var isConnected = await mcpClientService.TestConnectionAsync(serverConfig);
                        connectivityResults.Add((evaluation.Name, serverConfig.Path ?? serverConfig.Url ?? "Unknown", isConnected, null));

                        if (isConnected)
                        {
                            Console.WriteLine($"   ✅ {evaluation.Name}: Connected successfully");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ {evaluation.Name}: Connection failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        var serverConfig = configuration.Server;
                        connectivityResults.Add((evaluation.Name, serverConfig.Path ?? serverConfig.Url ?? "Unknown", false, ex.Message));
                        Console.WriteLine($"   ❌ {evaluation.Name}: {ex.Message}");
                    }
                }

                var failedConnections = connectivityResults.Where(r => !r.IsConnected).ToList();
                if (failedConnections.Any())
                {
                    Console.Error.WriteLine($"\n❌ {failedConnections.Count} server(s) failed connectivity test");
                    return 1;
                }

                Console.WriteLine($"\n✅ All {connectivityResults.Count} servers are reachable");
            }

            // Summary
            Console.WriteLine($"\n🎉 Configuration validation completed successfully!");
            Console.WriteLine($"   - Configuration file: ✅ Valid");
            Console.WriteLine($"   - Evaluations: ✅ {configuration.Evaluations.Count} valid");
            Console.WriteLine($"   - Server files: ✅ All exist");

            if (checkConnectivity)
            {
                Console.WriteLine($"   - Connectivity: ✅ All servers reachable");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Validation failed: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            return 1;
        }
    }

    private static bool IsValidServerPath(ServerConfiguration server)
    {
        if (server.Transport == "http")
        {
            // For HTTP transport, URL is always required
            if (string.IsNullOrEmpty(server.Url))
            {
                return false; // URL is required for HTTP transport
            }

            // Check if URL is valid
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return false; // Invalid URL
            }

            // If path is provided, check if server file exists (server startup mode)
            // If no path provided, assume direct URL connection (server already running)
            if (!string.IsNullOrEmpty(server.Path))
            {
                return File.Exists(server.Path);
            }

            return true; // Direct URL connection is valid
        }

        // For stdio transport, path is required and file must exist
        return !string.IsNullOrEmpty(server.Path) && File.Exists(server.Path);
    }

    private static string GetServerPathError(ServerConfiguration server)
    {
        if (server.Transport == "http")
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(server.Url))
            {
                errors.Add("URL is required for HTTP transport");
            }
            else if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                errors.Add($"Invalid URL: {server.Url}");
            }

            // Only check path if it's provided (optional for HTTP)
            if (!string.IsNullOrEmpty(server.Path) && !File.Exists(server.Path))
            {
                errors.Add($"Server file not found at {server.Path}");
            }

            return errors.Any() ? string.Join("; ", errors) : "Valid HTTP configuration";
        }
        else
        {
            if (string.IsNullOrEmpty(server.Path))
            {
                return "Path is required for stdio transport";
            }
            return $"Server file not found at {server.Path}";
        }
    }

    /// <summary>
    /// Create a configured host builder with command-line options instead of environment variables
    /// </summary>
    private static IHostBuilder CreateHostBuilderWithOptions(bool verbose)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Warning;
                });
                logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                // Add MCP Evals services with minimal options for validation
                services.AddMcpEvaluations();

                // Add command options for services
                services.AddSingleton(new EvaluationCommandOptions
                {
                    Verbose = verbose
                });
            });

        return builder;
    }
}