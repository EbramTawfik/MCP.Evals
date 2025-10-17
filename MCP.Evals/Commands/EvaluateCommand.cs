using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCP.Evals.Abstractions;
using MCP.Evals.Extensions;
using MCP.Evals.Models;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.Json;

namespace MCP.Evals.Commands;

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
            () => "clean",
            "Output format: json, summary, detailed, or clean");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose logging");

        var parallelOption = new Option<int>(
            ["--parallel", "-p"],
            () => Environment.ProcessorCount,
            "Maximum number of parallel evaluations");

        var apiKeyOption = new Option<string?>(
            ["--api-key"],
            "API key for the language model provider (OpenAI, Azure OpenAI, or Anthropic)");

        var endpointOption = new Option<string?>(
            ["--endpoint"],
            "Custom endpoint URL (for Azure OpenAI or other custom providers)");

        var enableMetricsOption = new Option<bool>(
            ["--enable-metrics"],
            "Enable Prometheus metrics collection");

        AddArgument(configPathArgument);
        AddOption(outputOption);
        AddOption(formatOption);
        AddOption(verboseOption);
        AddOption(parallelOption);
        AddOption(apiKeyOption);
        AddOption(endpointOption);
        AddOption(enableMetricsOption);

        this.SetHandler(async (context) =>
        {
            var args = new EvaluateCommandArgs
            {
                ConfigPath = context.ParseResult.GetValueForArgument(configPathArgument),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                Format = context.ParseResult.GetValueForOption(formatOption) ?? "clean",
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                Parallel = context.ParseResult.GetValueForOption(parallelOption),
                ApiKey = context.ParseResult.GetValueForOption(apiKeyOption),
                Endpoint = context.ParseResult.GetValueForOption(endpointOption),
                EnableMetrics = context.ParseResult.GetValueForOption(enableMetricsOption)
            };

            var result = await ExecuteAsync(args);
            context.ExitCode = result;
        });
    }

    private static async Task<int> ExecuteAsync(EvaluateCommandArgs args)
    {
        try
        {
            if (args.Verbose)
            {
                Console.WriteLine($"[INFO] Starting evaluation with config: {args.ConfigPath}");
                Console.WriteLine($"[INFO] Current Directory: {Environment.CurrentDirectory}");
                Console.WriteLine($"[INFO] Full Config Path: {Path.GetFullPath(args.ConfigPath)}");
            }

            // Build and configure the host with command-line options
            var hostBuilder = CreateHostBuilderWithOptions(args.Verbose, args.EnableMetrics, args.ApiKey, args.Endpoint);

            if (args.Verbose)
            {
                hostBuilder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.AddConsole();
                });
            }
            else
            {
                hostBuilder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Error;
                    });
                });
            }

            using var host = hostBuilder.Build();

            var logger = host.Services.GetRequiredService<ILogger<EvaluateCommand>>();
            var orchestrator = host.Services.GetRequiredService<IEvaluationOrchestrationService>();

            if (args.Verbose)
            {
                logger.LogInformation("Starting MCP evaluations from: {ConfigPath}", args.ConfigPath);
            }
            else
            {
                Console.WriteLine($"🚀 Starting evaluations from: {Path.GetFileName(args.ConfigPath)}");
            }

            if (!File.Exists(args.ConfigPath))
            {
                Console.WriteLine($"[ERROR] Configuration file not found: {args.ConfigPath}");
                Console.WriteLine($"[ERROR] Absolute path: {Path.GetFullPath(args.ConfigPath)}");
                logger.LogError("Configuration file not found: {ConfigPath}", args.ConfigPath);
                return 1;
            }

            if (args.Verbose)
            {
                var configContent = await File.ReadAllTextAsync(args.ConfigPath);
                Console.WriteLine($"[INFO] Config file loaded successfully ({configContent.Length} chars)");
            }

            var stopwatch = Stopwatch.StartNew();

            // Run evaluations
            var results = await orchestrator.RunEvaluationsFromFileAsync(args.ConfigPath);

            stopwatch.Stop();

            // Generate output
            await GenerateOutputAsync(results, args.OutputPath, args.Format, args.ConfigPath, logger, stopwatch.Elapsed);

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
            if (args.Verbose)
            {
                Console.Error.WriteLine($"[ERROR] Full exception: {ex}");
            }
            return 1;
        }
    }

    private static async Task GenerateOutputAsync(
        IReadOnlyList<Models.EvaluationResult> results,
        string? outputPath,
        string format,
        string configPath,
        ILogger logger,
        TimeSpan totalDuration)
    {
        string output = format.ToLower() switch
        {
            "json" => GenerateJsonOutput(results, totalDuration),
            "summary" => GenerateSummaryOutput(results, totalDuration),
            "detailed" => GenerateDetailedOutput(results, totalDuration),
            "clean" => GenerateCleanOutput(results, totalDuration),
            _ => throw new ArgumentException($"Unsupported output format: {format}")
        };

        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine(output);

            // For clean format, automatically save as markdown file based on config name
            if (format.ToLower() == "clean")
            {
                var configFileName = Path.GetFileNameWithoutExtension(configPath);
                var markdownPath = Path.Combine(Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory, $"{configFileName}.md");

                // Save the same output to file (already in markdown format)
                await File.WriteAllTextAsync(markdownPath, output);
                logger.LogInformation("Results automatically saved to markdown file: {MarkdownPath}", markdownPath);
                Console.WriteLine($"📄 Results saved to: {markdownPath}");
            }
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, output);
            logger.LogInformation("Results written to: {OutputPath}", outputPath);
        }
    }

    private static string GenerateJsonOutput(IReadOnlyList<Models.EvaluationResult> results, TimeSpan totalDuration)
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
                r.Response,
                ResponseLength = r.Response?.Length ?? 0
            })
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string GenerateSummaryOutput(IReadOnlyList<Models.EvaluationResult> results, TimeSpan totalDuration)
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

    private static string GenerateCleanOutput(IReadOnlyList<Models.EvaluationResult> results, TimeSpan totalDuration)
    {
        var successful = results.Where(r => r.IsSuccess).ToList();
        var failed = results.Where(r => !r.IsSuccess).ToList();
        var averageScore = successful.DefaultIfEmpty().Average(r => r?.Score.AverageScore ?? 0);

        var output = "# 🔍 MCP Evaluation Results\n\n";
        output += $"*Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*\n\n";

        // Summary section
        output += "## 📊 Summary\n\n";
        output += $"- **Total Evaluations:** {results.Count}\n";
        output += $"- **Successful:** {successful.Count} ✅\n";
        output += $"- **Failed:** {failed.Count} ❌\n";
        output += $"- **Success Rate:** {(successful.Count / (double)results.Count):P1}\n";
        output += $"- **Average Score:** {averageScore:F2}/5.0 {GetScoreEmoji(averageScore)}\n";
        output += $"- **Total Duration:** {totalDuration.TotalSeconds:F1} seconds\n\n";

        // Individual results
        output += "## 📋 Detailed Results\n\n";

        foreach (var result in results)
        {
            if (result.IsSuccess)
            {
                output += $"### ✅ {result.Name}\n\n";
                output += $"**Score:** {result.Score.AverageScore:F1}/5.0 {GetScoreEmoji(result.Score.AverageScore)}  \n";
                output += $"**Duration:** {result.Duration.TotalSeconds:F1}s  \n\n";

                // Score breakdown table
                output += "| Metric | Score |\n";
                output += "|--------|-------|\n";
                output += $"| Accuracy | {result.Score.Accuracy}/5 |\n";
                output += $"| Completeness | {result.Score.Completeness}/5 |\n";
                output += $"| Relevance | {result.Score.Relevance}/5 |\n";
                output += $"| Clarity | {result.Score.Clarity}/5 |\n";
                output += $"| Reasoning | {result.Score.Reasoning}/5 |\n\n";

                // Test details
                output += "**Test Prompt:**\n";
                output += $"> {result.Prompt}\n\n";

                if (!string.IsNullOrEmpty(result.Response))
                {
                    output += "**Response:**\n";
                    var responsePreview = result.Response.Length > 200
                        ? result.Response.Substring(0, 200) + "..."
                        : result.Response;
                    output += $"```\n{responsePreview}\n```\n\n";
                }

                // Add reasoning/comments if available and not default
                if (!string.IsNullOrEmpty(result.Score.OverallComments) &&
                    result.Score.OverallComments != "No comments provided")
                {
                    output += "**Evaluation Comments:**\n";
                    output += $"> {result.Score.OverallComments}\n\n";
                }
            }
            else
            {
                output += $"### ❌ {result.Name}\n\n";
                output += $"**Error:** {result.ErrorMessage}  \n";
                output += $"**Duration:** {result.Duration.TotalSeconds:F1}s  \n\n";
            }

            output += "---\n\n";
        }

        return output;
    }

    private static string GetScoreEmoji(double score)
    {
        return score switch
        {
            >= 4.5 => "🌟 Excellent",
            >= 3.5 => "🟢 Good",
            >= 2.5 => "🟡 Fair",
            >= 1.5 => "🟠 Poor",
            _ => "🔴 Critical"
        };
    }

    private static string GenerateDetailedOutput(IReadOnlyList<Models.EvaluationResult> results, TimeSpan totalDuration)
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

    /// <summary>
    /// Create a configured host builder with command-line options instead of environment variables
    /// </summary>
    private static IHostBuilder CreateHostBuilderWithOptions(
        bool verbose,
        bool enableMetrics,
        string? apiKey,
        string? endpoint)
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
                // Add MCP Evals services using our extension method with options
                services.AddMcpEvaluations(options =>
                {
                    if (verbose)
                    {
                        Console.WriteLine($"[DEBUG] Configuring services with command-line options");
                        Console.WriteLine($"[DEBUG] API Key: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : "SET")}");
                        Console.WriteLine($"[DEBUG] Endpoint: {(string.IsNullOrEmpty(endpoint) ? "NOT SET" : "SET")}");
                    }

                    // Configure language model with provided API key if available
                    // The provider and model name will be determined from the YAML configuration
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        options.DefaultLanguageModel = new LanguageModelConfiguration
                        {
                            Provider = "azure-openai", // Use Azure OpenAI when API key is provided via command line
                            Name = "gpt-4o",           // Default model - will be overridden by YAML
                            ApiKey = apiKey,
                            MaxTokens = 4000,
                            Temperature = 0.1
                        };
                    }

                    // Enable Prometheus metrics if requested
                    options.EnablePrometheusMetrics = enableMetrics;
                });

                // Add additional services with the command-line options
                services.AddSingleton(new EvaluationCommandOptions
                {
                    Verbose = verbose,
                    ApiKey = apiKey,
                    Endpoint = endpoint,
                    EnableMetrics = enableMetrics
                });
            });

        return builder;
    }
}