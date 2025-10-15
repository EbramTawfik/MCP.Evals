using FluentValidation;
using MCP.Evals.Core.Models;

namespace MCP.Evals.Core.Validation;

/// <summary>
/// Validator for EvaluationRequest following SRP
/// </summary>
public class EvaluationRequestValidator : AbstractValidator<EvaluationRequest>
{
    public EvaluationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Evaluation name is required")
            .MaximumLength(100)
            .WithMessage("Evaluation name must not exceed 100 characters");

        RuleFor(x => x.Description)
            .NotEmpty()
            .WithMessage("Evaluation description is required")
            .MaximumLength(500)
            .WithMessage("Evaluation description must not exceed 500 characters");

        RuleFor(x => x.Prompt)
            .NotEmpty()
            .WithMessage("Evaluation prompt is required")
            .MaximumLength(10000)
            .WithMessage("Evaluation prompt must not exceed 10000 characters");

        RuleFor(x => x.ServerPath)
            .NotEmpty()
            .WithMessage("Server path is required")
            .Must(BeValidPath)
            .WithMessage("Server path must be a valid file path");
    }

    private static bool BeValidPath(string path)
    {
        try
        {
            // For testing purposes, accept any non-empty path that looks like a file path
            // In a real scenario, you might want to check if file exists
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.Contains('/') || path.Contains('\\') || Path.IsPathFullyQualified(path));
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Validator for EvaluationConfiguration
/// </summary>
public class EvaluationConfigurationValidator : AbstractValidator<EvaluationConfiguration>
{
    public EvaluationConfigurationValidator()
    {
        RuleFor(x => x.Model)
            .NotNull()
            .WithMessage("Language model configuration is required")
            .SetValidator(new LanguageModelConfigurationValidator());

        RuleFor(x => x.Evaluations)
            .NotNull()
            .WithMessage("Evaluations list is required")
            .NotEmpty()
            .WithMessage("At least one evaluation is required");

        RuleForEach(x => x.Evaluations)
            .SetValidator(new EvaluationRequestValidator());
    }
}

/// <summary>
/// Validator for LanguageModelConfiguration
/// </summary>
public class LanguageModelConfigurationValidator : AbstractValidator<LanguageModelConfiguration>
{
    private static readonly string[] ValidProviders = { "openai", "anthropic", "azure-openai" };

    public LanguageModelConfigurationValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .WithMessage("Provider is required")
            .Must(provider => !string.IsNullOrEmpty(provider) && ValidProviders.Contains(provider.ToLower()))
            .WithMessage($"Provider must be one of: {string.Join(", ", ValidProviders)}");

        RuleFor(x => x.ModelName)
            .NotEmpty()
            .WithMessage("Model name is required");

        RuleFor(x => x.MaxTokens)
            .GreaterThan(0)
            .WithMessage("MaxTokens must be greater than 0")
            .LessThanOrEqualTo(100000)
            .WithMessage("MaxTokens must not exceed 100000");

        RuleFor(x => x.Temperature)
            .GreaterThanOrEqualTo(0.0)
            .WithMessage("Temperature must be >= 0.0")
            .LessThanOrEqualTo(2.0)
            .WithMessage("Temperature must be <= 2.0");

        RuleFor(x => x.MaxRetries)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MaxRetries must be >= 0")
            .LessThanOrEqualTo(10)
            .WithMessage("MaxRetries must be <= 10");

        RuleFor(x => x.Timeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Timeout must be greater than zero")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(10))
            .WithMessage("Timeout must not exceed 10 minutes");
    }
}