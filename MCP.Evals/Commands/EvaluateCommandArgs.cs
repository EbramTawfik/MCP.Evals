namespace MCP.Evals.Commands;

/// <summary>
/// Configuration options for the evaluate command
/// </summary>
public class EvaluateCommandArgs
{
    public string ConfigPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string Format { get; set; } = "clean";
    public bool Verbose { get; set; }
    public int Parallel { get; set; } = Environment.ProcessorCount;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public bool EnableMetrics { get; set; }
}