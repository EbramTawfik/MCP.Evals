using FluentAssertions;
using MCP.Evals.Core.Models;
using Xunit;

namespace MCP.Evals.Tests.Unit.Models;

/// <summary>
/// Unit tests for core models demonstrating domain logic validation
/// </summary>
public class EvaluationModelsTests
{
    [Fact]
    public void EvaluationScore_Should_Calculate_Correct_Average()
    {
        // Arrange & Act
        var score = new EvaluationScore
        {
            Accuracy = 4,
            Completeness = 5,
            Relevance = 3,
            Clarity = 4,
            Reasoning = 4,
            OverallComments = "Good performance overall"
        };

        // Assert
        score.AverageScore.Should().Be(4.0);
    }

    [Fact]
    public void EvaluationScore_Should_Throw_For_Invalid_Scores()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationScore
        {
            Accuracy = 0, // Invalid - should be 1-5
            Completeness = 3,
            Relevance = 4,
            Clarity = 5,
            Reasoning = 2,
            OverallComments = "Test"
        });
    }

    [Fact]
    public void EvaluationScore_Should_Throw_For_Scores_Above_Range()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationScore
        {
            Accuracy = 3,
            Completeness = 6, // Invalid - should be 1-5
            Relevance = 4,
            Clarity = 5,
            Reasoning = 2,
            OverallComments = "Test"
        });
    }

    [Fact]
    public void EvaluationResult_IsSuccess_Should_Return_True_When_No_Error()
    {
        // Arrange
        var result = new EvaluationResult
        {
            Name = "Test",
            Description = "Test evaluation",
            Prompt = "Test prompt",
            Response = "Test response",
            Score = new EvaluationScore
            {
                Accuracy = 4,
                Completeness = 4,
                Relevance = 4,
                Clarity = 4,
                Reasoning = 4,
                OverallComments = "Test"
            },
            Duration = TimeSpan.FromSeconds(1),
            ErrorMessage = null
        };

        // Act & Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EvaluationResult_IsSuccess_Should_Return_False_When_Error_Present()
    {
        // Arrange
        var result = new EvaluationResult
        {
            Name = "Test",
            Description = "Test evaluation",
            Prompt = "Test prompt",
            Response = "Test response",
            Score = new EvaluationScore
            {
                Accuracy = 4,
                Completeness = 4,
                Relevance = 4,
                Clarity = 4,
                Reasoning = 4,
                OverallComments = "Test"
            },
            Duration = TimeSpan.FromSeconds(1),
            ErrorMessage = "Something went wrong"
        };

        // Act & Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void EvaluationResult_Should_Set_Timestamp_To_Current_Time()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var result = new EvaluationResult
        {
            Name = "Test",
            Description = "Test evaluation",
            Prompt = "Test prompt",
            Response = "Test response",
            Score = new EvaluationScore
            {
                Accuracy = 4,
                Completeness = 4,
                Relevance = 4,
                Clarity = 4,
                Reasoning = 4,
                OverallComments = "Test"
            },
            Duration = TimeSpan.FromSeconds(1)
        };

        var after = DateTime.UtcNow;

        // Assert
        result.Timestamp.Should().BeOnOrAfter(before);
        result.Timestamp.Should().BeOnOrBefore(after);
    }
}