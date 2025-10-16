using FluentValidation;
using MCP.Evals.Models;

namespace MCP.Evals.Services;

/// <summary>
/// Validator for evaluation requests
/// </summary>
public class EvaluationRequestValidator : AbstractValidator<EvaluationRequest>
{
    public EvaluationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Prompt)
            .NotEmpty()
            .MaximumLength(10000);
    }
}

/// <summary>
/// Validator for evaluation configuration
/// </summary>
public class EvaluationConfigurationValidator : AbstractValidator<EvaluationConfiguration>
{
    public EvaluationConfigurationValidator()
    {
        RuleFor(x => x.Model)
            .NotNull();

        RuleFor(x => x.Server)
            .NotNull();

        RuleFor(x => x.Evaluations)
            .NotNull()
            .NotEmpty();

        RuleForEach(x => x.Evaluations)
            .SetValidator(new EvaluationRequestValidator());
    }
}

/// <summary>
/// Validator for language model configuration
/// </summary>
public class LanguageModelConfigurationValidator : AbstractValidator<LanguageModelConfiguration>
{
    public LanguageModelConfigurationValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .Must(p => p.ToLower() is "openai" or "anthropic" or "azure-openai")
            .WithMessage("Provider must be 'openai', 'anthropic', or 'azure-openai'");

        RuleFor(x => x.Name)
            .NotEmpty();

        RuleFor(x => x.MaxTokens)
            .GreaterThan(0)
            .LessThanOrEqualTo(100000);

        RuleFor(x => x.Temperature)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(2);
    }
}