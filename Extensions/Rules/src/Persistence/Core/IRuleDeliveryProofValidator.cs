using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

/// <summary>Verifies durable native consumption proof against the exact frozen Rules delivery.</summary>
public interface IRuleDeliveryProofValidator
{
    ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(
        RuleTerminalEnvelope envelope,
        RuleDeliveryProof proof,
        CancellationToken cancellationToken = default);
}

/// <summary>A bounded result from validating native parent state; exceptions are treated as unavailable.</summary>
public sealed record RuleDeliveryProofValidationResult(RuleDeliveryProofValidationDisposition Disposition);

public enum RuleDeliveryProofValidationDisposition
{
    Equivalent,
    Unavailable,
    Invalid
}
