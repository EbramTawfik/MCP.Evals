using Ardalis.GuardClauses;

namespace MCP.Evals.Models;

/// <summary>
/// Detailed scoring metrics for an evaluation
/// </summary>
public sealed class EvaluationScore
{
    private int _accuracy;
    private int _completeness;
    private int _relevance;
    private int _clarity;
    private int _reasoning;

    public required int Accuracy
    {
        get => _accuracy;
        init
        {
            Guard.Against.OutOfRange(value, nameof(Accuracy), 1, 5);
            _accuracy = value;
        }
    }

    public required int Completeness
    {
        get => _completeness;
        init
        {
            Guard.Against.OutOfRange(value, nameof(Completeness), 1, 5);
            _completeness = value;
        }
    }

    public required int Relevance
    {
        get => _relevance;
        init
        {
            Guard.Against.OutOfRange(value, nameof(Relevance), 1, 5);
            _relevance = value;
        }
    }

    public required int Clarity
    {
        get => _clarity;
        init
        {
            Guard.Against.OutOfRange(value, nameof(Clarity), 1, 5);
            _clarity = value;
        }
    }

    public required int Reasoning
    {
        get => _reasoning;
        init
        {
            Guard.Against.OutOfRange(value, nameof(Reasoning), 1, 5);
            _reasoning = value;
        }
    }

    public required string OverallComments { get; init; }

    public double AverageScore => (Accuracy + Completeness + Relevance + Clarity + Reasoning) / 5.0;
}