using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>
/// The safe diagnostic subset that may accompany a publication rejection.
/// Runtime values and provider-only related-symbol metadata are deliberately excluded.
/// </summary>
public sealed record ExpressionPublicationValidationDiagnostic(
    string Code,
    string Severity,
    string Message,
    string DocumentRevision,
    ExpressionToolingRange? Range,
    string? AuthoredPath);

/// <summary>Thrown when the current full-draft expression validation cannot authorize publication.</summary>
public sealed class ExpressionPublicationValidationException : InvalidOperationException
{
    public ExpressionPublicationValidationException(ExpressionDraftValidationResult validation)
        : base(CreateMessage(validation))
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.State == ExpressionDraftValidationState.Valid)
            throw new ArgumentException("A valid expression-validation result cannot reject publication.", nameof(validation));

        State = validation.State;
        Code = string.IsNullOrWhiteSpace(validation.Code)
            ? $"expression-validation-{validation.State.ToString().ToLowerInvariant()}"
            : validation.Code;
        Diagnostics = validation.Diagnostics.Select(ToSafeDiagnostic).ToArray();
    }

    public ExpressionDraftValidationState State { get; }
    public string Code { get; }
    public IReadOnlyList<ExpressionPublicationValidationDiagnostic> Diagnostics { get; }

    private static string CreateMessage(ExpressionDraftValidationResult validation) =>
        validation.State == ExpressionDraftValidationState.Unavailable
            ? "Expression validation is unavailable; publication is blocked."
            : $"Expression validation state '{validation.State}' blocks publication.";

    private static ExpressionPublicationValidationDiagnostic ToSafeDiagnostic(ExpressionDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Severity.ToString(),
        diagnostic.Message,
        diagnostic.DocumentRevision,
        diagnostic.Range,
        diagnostic.AuthoredPath);
}
