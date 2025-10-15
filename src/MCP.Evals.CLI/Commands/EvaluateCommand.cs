using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.Core.Interfaces;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace MCP.Evals.CLI.Commands;

/// <summary>
/// Evaluate command following SRP - only responsible for evaluation operations
/// </summary>
public class EvaluateCommand : Command
{
    public EvaluateCommand() : base("evaluate", "Run MCP evaluations from a configuration file")
    {
        var configPathArgument = new Argument<string>(
            "config-path",
            "Path to the evaluation configuration file (.yaml, .yml, or .json)");

        var outputOption = new Option<string?>(
            ["--output", "-o"],
            "Output file path for results (JSON format). If not specified, results are written to console.");

        var formatOption = new Option<string>(
            ["--format", "-f"],
            () => "json",
            "Output format: json, summary, or detailed");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose logging");

        var parallelOption = new Option<int>(
            ["--parallel", "-p"],
            () => Environment.ProcessorCount,
            "Maximum number of parallel evaluations");

        AddArgument(configPathArgument);
        AddOption(outputOption);
        AddOption(formatOption);
        AddOption(verboseOption);
        AddOption(parallelOption);

        this.SetHandler(ExecuteAsync, configPathArgument, outputOption, formatOption, verboseOption, parallelOption);
    }

    private static async Task<int> ExecuteAsync(
        string configPath,
        string? outputPath,
        string format,
        bool verbose,
        int parallel)
    {
        try
        {
            if (verbose)
            {
                Console.WriteLine($"[INFO] Starting evaluation with config: {configPath}");
                Console.WriteLine($"[INFO] Current Directory: {Environment.CurrentDirectory}");
                Console.WriteLine($"[INFO] Full Config Path: {Path.GetFullPath(configPath)}");
            }

            // Build and configure the host
            var hostBuilder = Program.CreateHostBuilder();

            if (verbose)
            {
                hostBuilder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.AddConsole();
                });
            }

            using var host = hostBuilder.Build();

            var logger = host.Services.GetRequiredService<ILogger<EvaluateCommand>>();
            var orchestrator = host.Services.GetRequiredService<IEvaluationOrchestrator>();

            logger.LogInformation("Starting MCP evaluations from: {ConfigPath}", configPath);

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[ERROR] Configuration file not found: {configPath}");
                Console.WriteLine($"[ERROR] Absolute path: {Path.GetFullPath(configPath)}");
                logger.LogError("Configuration file not found: {ConfigPath}", configPath);
                return 1;
            }

            if (verbose)
            {
                var configContent = await File.ReadAllTextAsync(configPath);
                Console.WriteLine($"[INFO] Config file loaded successfully ({configContent.Length} chars)");
            }

            var stopwatch = Stopwatch.StartNew();

            // Run evaluations
            var results = await orchestrator.RunEvaluationsFromFileAsync(configPath);

            stopwatch.Stop();

            // Generate output
            await GenerateOutputAsync(results, outputPath, format, logger, stopwatch.Elapsed);

            // Return exit code based on results
            var failedCount = results.Count(r => !r.IsSuccess);
            if (failedCount > 0)
            {
                logger.LogWarning("Evaluation completed with {FailedCount} failures", failedCount);
                return 1;
            }

            logger.LogInformation("All evaluations completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Evaluation failed: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine($"[ERROR] Full exception: {ex}");
            }
            return 1;
        }
    }

    private static async Task GenerateOutputAsync(
        IReadOnlyList<Core.Models.EvaluationResult> results,
        string? outputPath,
        string format,
        ILogger logger,
        TimeSpan totalDuration)
    {
        string output = format.ToLower() switch
        {
            "json" => GenerateJsonOutput(results, totalDuration),
            "summary" => GenerateSummaryOutput(results, totalDuration),
            "detailed" => GenerateDetailedOutput(results, totalDuration),
            _ => throw new ArgumentException($"Unsupported output format: {format}")
        };

        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine(output);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, output);
            logger.LogInformation("Results written to: {OutputPath}", outputPath);
        }
    }

    private static string GenerateJsonOutput(IReadOnlyList<Core.Models.EvaluationResult> results, TimeSpan totalDuration)
    {
        var output = new
        {
            Summary = new
            {
                TotalEvaluations = results.Count,
                SuccessfulEvaluations = results.Count(r => r.IsSuccess),
                FailedEvaluations = results.Count(r => !r.IsSuccess),
                AverageScore = results.Where(r => r.IsSuccess).DefaultIfEmpty().Average(r => r?.Score.AverageScore ?? 0),
                TotalDuration = totalDuration.TotalSeconds,
                Timestamp = DateTime.UtcNow
            },
            Results = results.Select(r => new
            {
                r.Name,
                r.Description,
                r.IsSuccess,
                r.ErrorMessage,
                Duration = r.Duration.TotalSeconds,
                Score = r.IsSuccess ? new
                {
                    r.Score.Accuracy,
                    r.Score.Completeness,
                    r.Score.Relevance,
                    r.Score.Clarity,
                    r.Score.Reasoning,
                    Average = r.Score.AverageScore,
                    r.Score.OverallComments
                } : null,
                r.Prompt,
                ResponseLength = r.Response?.Length ?? 0
            })
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string GenerateSummaryOutput(IReadOnlyList<Core.Models.EvaluationResult> results, TimeSpan totalDuration)
    {
        var successful = results.Where(r => r.IsSuccess).ToList();
        var failed = results.Where(r => !r.IsSuccess).ToList();

        var summary = $"""
            MCP Evaluations Summary
            ======================
            
            Total Evaluations: {results.Count}
            Successful: {successful.Count}
            Failed: {failed.Count}
            Success Rate: {(successful.Count / (double)results.Count):P1}
            
            Average Score: {(successful.DefaultIfEmpty().Average(r => r?.Score.AverageScore ?? 0)):F2}/5.0
            Total Duration: {totalDuration.TotalSeconds:F1} seconds
            
            """;

        if (failed.Any())
        {
            summary += "Failed Evaluations:\n";
            foreach (var failure in failed)
            {
                summary += $"  ❌ {failure.Name}: {failure.ErrorMessage}\n";
            }
            summary += "\n";
        }

        summary += "Successful Evaluations:\n";
        foreach (var success in successful.OrderByDescending(r => r.Score.AverageScore))
        {
            summary += $"  ✅ {success.Name}: {success.Score.AverageScore:F2}/5.0 ({success.Duration.TotalSeconds:F1}s)\n";
        }

        return summary;
    }

    private static string GenerateDetailedOutput(IReadOnlyList<Core.Models.EvaluationResult> results, TimeSpan totalDuration)
    {
        var output = GenerateSummaryOutput(results, totalDuration);

        output += "\n\nDetailed Results:\n";
        output += "================\n\n";

        foreach (var result in results)
        {
            output += $"Evaluation: {result.Name}\n";
            output += $"Description: {result.Description}\n";
            output += $"Status: {(result.IsSuccess ? "✅ Success" : "❌ Failed")}\n";
            output += $"Duration: {result.Duration.TotalSeconds:F2} seconds\n";

            if (result.IsSuccess)
            {
                output += $"Scores:\n";
                output += $"  Accuracy: {result.Score.Accuracy}/5\n";
                output += $"  Completeness: {result.Score.Completeness}/5\n";
                output += $"  Relevance: {result.Score.Relevance}/5\n";
                output += $"  Clarity: {result.Score.Clarity}/5\n";
                output += $"  Reasoning: {result.Score.Reasoning}/5\n";
                output += $"  Average: {result.Score.AverageScore:F2}/5\n";
                output += $"Comments: {result.Score.OverallComments}\n";
            }
            else
            {
                output += $"Error: {result.ErrorMessage}\n";
            }

            output += $"Prompt: {result.Prompt.Substring(0, Math.Min(100, result.Prompt.Length))}{(result.Prompt.Length > 100 ? "..." : "")}\n";

            if (!string.IsNullOrEmpty(result.Response))
            {
                output += $"Response: {result.Response.Substring(0, Math.Min(200, result.Response.Length))}{(result.Response.Length > 200 ? "..." : "")}\n";
            }

            output += "\n" + new string('-', 50) + "\n\n";
        }

        return output;
    }
}