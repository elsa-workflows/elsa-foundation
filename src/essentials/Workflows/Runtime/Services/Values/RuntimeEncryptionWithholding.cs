using System.Diagnostics.CodeAnalysis;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The producer withholding rule of spec 188, FR-010, in one place. Phase 0 has no encryption at rest, so a present value
/// whose policy requires encryption is replaced, before any inline or external storage decision, by a
/// <see cref="WithheldValueKind.PolicyRequiresEncryption"/> marker that holds no value. The value cannot be recovered; a
/// reader that needs it refuses the marker with <c>VF-ACT-010</c>.
/// </summary>
/// <remarks>
/// Its callers are <see cref="RuntimeExternalEnvelopeStorage"/>, the destination-storage decision for activity input
/// materialization and intrinsic value writes, and the activity completion projector in <c>Elsa.Activities.Runtime</c>,
/// which writes an activity result to external payload storage itself.
/// </remarks>
public static class RuntimeEncryptionWithholding
{
    /// <summary>
    /// Withholds <paramref name="value"/> when it is present and either <paramref name="destinationPolicy"/> or the
    /// value's own policy requires encryption. The destination's policy decides first, and the value's own policy is
    /// honored too, so a value that already requires encryption is never released by a destination that does not say so.
    /// The marker keeps the policy that required it.
    /// </summary>
    public static bool TryWithhold(
        ValueEnvelope value,
        ValueProtectionPolicy destinationPolicy,
        [NotNullWhen(true)] out ValueEnvelope? withheld)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(destinationPolicy);

        var policy = value.Presence != ValuePresence.Present
            ? null
            : destinationPolicy.RequiresEncryption
                ? destinationPolicy
                : value.Policy.RequiresEncryption
                    ? value.Policy
                    : null;
        withheld = policy is null
            ? null
            : ValueEnvelope.Withheld(value.Type, new WithheldValue(WithheldValueKind.PolicyRequiresEncryption), policy);
        return withheld is not null;
    }
}
