using MCP.Evals.Models;

namespace MCP.Evals.Extensions;

/// <summary>
/// Configuration options for MCP Evals
/// </summary>
public class McpEvalsOptions
{
    public LanguageModelConfiguration DefaultLanguageModel { get; set; } = new()
    {
        Provider = "openai",
        Name = "gpt-4o",
        MaxTokens = 4000,
        Temperature = 0.1
    };

    public bool EnablePrometheusMetrics { get; set; } = false;
    public int MaxConcurrentEvaluations { get; set; } = Environment.ProcessorCount;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
}