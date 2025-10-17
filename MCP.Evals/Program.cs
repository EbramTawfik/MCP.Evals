using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.Commands;
using MCP.Evals.Models;
using MCP.Evals.Extensions;
using Prometheus;
using System.CommandLine;

namespace MCP.Evals;

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
                    // Note: For the default host builder, we don't have command-line options available
                    // Individual commands will create their own host builders with specific options
                    // This is kept for compatibility with other uses of CreateHostBuilder
                });
            });

        return builder;
    }
}