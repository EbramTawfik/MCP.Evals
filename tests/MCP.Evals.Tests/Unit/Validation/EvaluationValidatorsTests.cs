using FluentAssertions;
using MCP.Evals.Core.Models;
using MCP.Evals.Core.Validation;
using Xunit;

namespace MCP.Evals.Tests.Unit.Validation;

/// <summary>
/// Unit tests for evaluation validators demonstrating SOLID principles
/// </summary>
public class EvaluationValidatorsTests
{
    [Fact]
    public void EvaluationRequestValidator_Should_Pass_For_Valid_Request()
    {
        // Arrange
        var validator = new EvaluationRequestValidator();
        var request = new EvaluationRequest
        {
            Name = "Test Evaluation",
            Description = "A test evaluation",
            Prompt = "What is the weather?"
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Evaluation name is required")]
    [InlineData(null, "Evaluation name is required")]
    public void EvaluationRequestValidator_Should_Fail_For_Invalid_Name(string? name, string expectedError)
    {
        // Arrange
        var validator = new EvaluationRequestValidator();
        var request = new EvaluationRequest
        {
            Name = name!,
            Description = "A test evaluation",
            Prompt = "What is the weather?"
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == expectedError);
    }

    [Fact]
    public void EvaluationRequestValidator_Should_Fail_For_Too_Long_Name()
    {
        // Arrange
        var validator = new EvaluationRequestValidator();
        var request = new EvaluationRequest
        {
            Name = new string('A', 101), // 101 characters
            Description = "A test evaluation",
            Prompt = "What is the weather?"
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Evaluation name must not exceed 100 characters");
    }

    [Fact]
    public void LanguageModelConfigurationValidator_Should_Pass_For_Valid_Configuration()
    {
        // Arrange
        var validator = new LanguageModelConfigurationValidator();
        var config = new LanguageModelConfiguration
        {
            Provider = "openai",
            Name = "gpt-4o",
            MaxTokens = 4000,
            Temperature = 0.1
        };

        // Act
        var result = validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-provider")]
    [InlineData("")]
    [InlineData(null)]
    public void LanguageModelConfigurationValidator_Should_Fail_For_Invalid_Provider(string? provider)
    {
        // Arrange
        var validator = new LanguageModelConfigurationValidator();
        var config = new LanguageModelConfiguration
        {
            Provider = provider!,
            Name = "gpt-4o"
        };

        // Act
        var result = validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void LanguageModelConfigurationValidator_Should_Fail_For_Invalid_MaxTokens(int maxTokens)
    {
        // Arrange
        var validator = new LanguageModelConfigurationValidator();
        var config = new LanguageModelConfiguration
        {
            Provider = "openai",
            Name = "gpt-4o",
            MaxTokens = maxTokens
        };

        // Act
        var result = validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void LanguageModelConfigurationValidator_Should_Fail_For_Invalid_Temperature(double temperature)
    {
        // Arrange
        var validator = new LanguageModelConfigurationValidator();
        var config = new LanguageModelConfiguration
        {
            Provider = "openai",
            Name = "gpt-4o",
            Temperature = temperature
        };

        // Act
        var result = validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}