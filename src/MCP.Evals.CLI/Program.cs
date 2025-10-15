using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.CLI.Commands;
using MCP.Evals.Core.Models;
using MCP.Evals.Infrastructure.Extensions;
using Prometheus;
using System.CommandLine;

namespace MCP.Evals.CLI;

/// <summary>
/// Main program entry point following SOLID principles
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Build the command line interface
        var rootCommand = new RootCommand("MCP Evals - Evaluation framework for MCP servers")
        {
            new EvaluateCommand(),
            new ValidateCommand(),
            new ServeMetricsCommand()
        };

        rootCommand.SetHandler(() =>
        {
            Console.WriteLine("MCP Evals - Use --help to see available commands");
            return Task.FromResult(0);
        });

        try
        {
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Fatal error: {ex.Message}");
            Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            return 1;
        }
    }

    /// <summary>
    /// Create a configured host builder following DIP
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[]? args = null)
    {
        var builder = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Warning;
                });
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                // Add MCP Evals services using our extension method
                services.AddMcpEvaluations(options =>
                {
                    // Configure based on environment variables or configuration
                    var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    Console.WriteLine($"[DEBUG] Reading API key from environment in Program.cs: {(string.IsNullOrEmpty(openAiApiKey) ? "NOT SET" : "SET")}");

                    if (!string.IsNullOrEmpty(openAiApiKey))
                    {
                        Console.WriteLine($"[DEBUG] Setting API key in configuration...");
                        // Create a new configuration with the API key
                        options.DefaultLanguageModel = new LanguageModelConfiguration
                        {
                            Provider = options.DefaultLanguageModel.Provider,
                            ModelName = options.DefaultLanguageModel.ModelName,
                            ApiKey = openAiApiKey,
                            MaxTokens = options.DefaultLanguageModel.MaxTokens,
                            Temperature = options.DefaultLanguageModel.Temperature
                        };
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] API key not found in environment, using default configuration");
                    }

                    // Enable Prometheus metrics if requested
                    var enableMetrics = Environment.GetEnvironmentVariable("ENABLE_PROMETHEUS_METRICS");
                    options.EnablePrometheusMetrics = bool.TryParse(enableMetrics, out var result) && result;
                });
            });

        return builder;
    }
}