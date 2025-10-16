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

        RuleFor(x => x.Server)
            .NotNull()
            .WithMessage("Server configuration is required")
            .SetValidator(new ServerConfigurationValidator());

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

        RuleFor(x => x.Name)
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


    }
}

/// <summary>
/// Validator for ServerConfiguration
/// </summary>
public class ServerConfigurationValidator : AbstractValidator<ServerConfiguration>
{
    private static readonly string[] ValidTransports = { "stdio", "http" };

    public ServerConfigurationValidator()
    {
        RuleFor(x => x.Transport)
            .NotEmpty()
            .WithMessage("Transport type is required")
            .Must(transport => !string.IsNullOrEmpty(transport) && ValidTransports.Contains(transport.ToLower()))
            .WithMessage($"Transport must be one of: {string.Join(", ", ValidTransports)}");

        // For stdio transport, Path is required
        RuleFor(x => x.Path)
            .NotEmpty()
            .WithMessage("Server path is required for stdio transport")
            .When(x => x.Transport?.ToLower() == "stdio");

        // For HTTP transport, either Path or Url is required (or both)
        RuleFor(x => x)
            .Must(config => !string.IsNullOrEmpty(config.Path) || !string.IsNullOrEmpty(config.Url))
            .WithMessage("For HTTP transport, either Path (server file) or Url (direct connection) must be specified")
            .When(x => x.Transport?.ToLower() == "http");

        // Validate URL format when provided
        RuleFor(x => x.Url)
            .Must(BeValidHttpUrl!)
            .WithMessage("Url must be a valid HTTP or HTTPS URL")
            .When(x => !string.IsNullOrEmpty(x.Url));

        // Validate Path format when provided
        RuleFor(x => x.Path)
            .Must(BeValidPath!)
            .WithMessage("Path must be a valid file path")
            .When(x => !string.IsNullOrEmpty(x.Path));

        RuleFor(x => x.Timeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Timeout must be greater than zero")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(10))
            .WithMessage("Timeout must not exceed 10 minutes");
    }

    private static bool BeValidPath(string path)
    {
        try
        {
            // Accept any non-empty path that looks like a file path
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.Contains('/') || path.Contains('\\') || Path.IsPathFullyQualified(path));
        }
        catch
        {
            return false;
        }
    }

    private static bool BeValidHttpUrl(string url)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == "http" || uri.Scheme == "https");
        }
        catch
        {
            return false;
        }
    }
}