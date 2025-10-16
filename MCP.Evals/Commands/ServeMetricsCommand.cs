using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using System.CommandLine;

namespace MCP.Evals.Commands;

/// <summary>
/// Serve metrics command following SRP - only responsible for metrics serving
/// </summary>
public class ServeMetricsCommand : Command
{
    public ServeMetricsCommand() : base("serve-metrics", "Start a Prometheus metrics server")
    {
        var portOption = new Option<int>(
            ["--port", "-p"],
            () => 9090,
            "Port to serve metrics on");

        var hostOption = new Option<string>(
            ["--host", "-h"],
            () => "localhost",
            "Host to bind to");

        AddOption(portOption);
        AddOption(hostOption);

        this.SetHandler(ExecuteAsync, portOption, hostOption);
    }

    private static async Task<int> ExecuteAsync(int port, string host)
    {
        try
        {
            Console.WriteLine($"🚀 Starting Prometheus metrics server on http://{host}:{port}/metrics");
            Console.WriteLine("Press Ctrl+C to stop the server");

            var builder = WebApplication.CreateBuilder();

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Configure Kestrel
            builder.WebHost.UseUrls($"http://{host}:{port}");

            var app = builder.Build();

            // Add Prometheus metrics endpoint
            app.UseRouting();
            app.MapMetrics("/metrics");

            // Add health check endpoint
            app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

            // Add root endpoint with basic information
            app.MapGet("/", () => new
            {
                Service = "MCP.Evals Metrics Server",
                Version = "1.0.0",
                Endpoints = new
                {
                    Metrics = "/metrics",
                    Health = "/health"
                },
                Timestamp = DateTime.UtcNow
            });

            // Handle shutdown gracefully
            var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                Console.WriteLine("\n🛑 Stopping metrics server...");
            };

            try
            {
                await app.RunAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutdown is requested
            }

            Console.WriteLine("✅ Metrics server stopped successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Failed to start metrics server: {ex.Message}");
            return 1;
        }
    }
}