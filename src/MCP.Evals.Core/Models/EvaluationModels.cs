using Ardalis.GuardClauses;

namespace MCP.Evals.Core.Models;

/// <summary>
/// Represents the result of an evaluation with detailed scoring metrics
/// </summary>
public sealed class EvaluationResult
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required string Response { get; init; }
    public required EvaluationScore Score { get; init; }
    public required TimeSpan Duration { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? ErrorMessage { get; init; }
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}

/// <summary>
/// Detailed scoring metrics for an evaluation
/// </summary>
public sealed class EvaluationScore
{
    public required int Accuracy { get; init; }        // 1-5 scale
    public required int Completeness { get; init; }    // 1-5 scale
    public required int Relevance { get; init; }       // 1-5 scale
    public required int Clarity { get; init; }         // 1-5 scale
    public required int Reasoning { get; init; }       // 1-5 scale
    public required string OverallComments { get; init; }

    public double AverageScore => (Accuracy + Completeness + Relevance + Clarity + Reasoning) / 5.0;

    public EvaluationScore()
    {
        // Validate scores are in valid range
        Guard.Against.OutOfRange(Accuracy, nameof(Accuracy), 1, 5);
        Guard.Against.OutOfRange(Completeness, nameof(Completeness), 1, 5);
        Guard.Against.OutOfRange(Relevance, nameof(Relevance), 1, 5);
        Guard.Against.OutOfRange(Clarity, nameof(Clarity), 1, 5);
        Guard.Against.OutOfRange(Reasoning, nameof(Reasoning), 1, 5);
    }
}

/// <summary>
/// Request for running an evaluation
/// </summary>
public sealed class EvaluationRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required string ServerPath { get; init; }
    public string? ExpectedResult { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Configuration for a complete evaluation suite
/// </summary>
public sealed class EvaluationConfiguration
{
    public required LanguageModelConfiguration Model { get; init; }
    public required IReadOnlyList<EvaluationRequest> Evaluations { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Configuration for language model
/// </summary>
public sealed class LanguageModelConfiguration
{
    public required string Provider { get; init; } // "openai", "anthropic", "azure-openai"
    public required string ModelName { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public int MaxTokens { get; init; } = 4000;
    public double Temperature { get; init; } = 0.1;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}