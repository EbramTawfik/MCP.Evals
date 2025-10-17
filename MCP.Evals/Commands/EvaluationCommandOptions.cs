namespace MCP.Evals.Commands;

/// <summary>
/// Options passed from command line to services to replace environment variables
/// </summary>
public class EvaluationCommandOptions
{
    public bool Verbose { get; set; }
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public bool EnableMetrics { get; set; }
}