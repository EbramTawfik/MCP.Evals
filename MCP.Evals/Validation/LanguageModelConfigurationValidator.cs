using FluentValidation;
using MCP.Evals.Models;

namespace MCP.Evals.Validation;

/// <summary>
/// Validator for language model configuration following SRP
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