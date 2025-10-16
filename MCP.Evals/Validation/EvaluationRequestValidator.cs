using FluentValidation;
using MCP.Evals.Models;

namespace MCP.Evals.Validation;

/// <summary>
/// Validator for evaluation requests following SRP
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