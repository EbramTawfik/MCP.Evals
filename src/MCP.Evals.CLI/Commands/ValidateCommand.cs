using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using System.CommandLine;

namespace MCP.Evals.CLI.Commands;

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
            // Build and configure the host
            var hostBuilder = Program.CreateHostBuilder();

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
                    Console.WriteLine($"   - Language model: {configuration.Model.Provider}/{configuration.Model.ModelName}");
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
            foreach (var evaluation in configuration.Evaluations)
            {
                if (!File.Exists(evaluation.ServerPath))
                {
                    missingServers.Add($"Evaluation '{evaluation.Name}': Server file not found at {evaluation.ServerPath}");
                }
            }

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
                        var isConnected = await mcpClientService.TestConnectionAsync(evaluation.ServerPath);
                        connectivityResults.Add((evaluation.Name, evaluation.ServerPath, isConnected, null));

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
                        connectivityResults.Add((evaluation.Name, evaluation.ServerPath, false, ex.Message));
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
}