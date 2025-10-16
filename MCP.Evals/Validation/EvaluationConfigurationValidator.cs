using FluentValidation;
using MCP.Evals.Models;

namespace MCP.Evals.Validation;

/// <summary>
/// Validator for evaluation configuration following SRP
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