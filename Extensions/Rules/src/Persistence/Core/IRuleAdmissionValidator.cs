using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

/// <summary>Replacement contract: one runtime semantic validator, called only after identity-first replay lookup.</summary>
/// <remarks>
/// The provider rechecks a concurrent key winner after validation, including on failure. This contract
/// performs no persistence and does not replace provider transaction, projection-size or destination-proof checks.
/// Cancellation and existing-identity replay use common record encoding without calling this validator.
/// </remarks>
public interface IRuleAdmissionValidator
{
    ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
        CancellationToken cancellationToken = default);
}

public enum RuleAdmissionValidationDisposition
{
    Ready,
    Refused,
    Invalid,
    Unavailable
}

/// <summary>A semantic admission decision; not a public input flag that can authorize a store mutation.</summary>
public sealed record RuleAdmissionValidationResult(RuleAdmissionValidationDisposition Disposition, RuleRunDiagnostic? Diagnostic);
