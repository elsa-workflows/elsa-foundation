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
/// materialization and intrinsic value writes, the activity completion projector in <c>Elsa.Activities.Runtime</c>,
/// which writes an activity result to external payload storage itself, and the output capture projector there, which
/// writes the marker into a captured workflow variable. The predicate is
/// <see cref="ValueEnvelope.HoldsValueRequiringEncryption"/> and the marker <see cref="WithheldValue.PolicyRequiresEncryption"/>,
/// both in <c>Elsa.Workflows.Runtime.Core</c>, so a module that cannot reach this class applies the same rule.
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

        withheld = value.HoldsValueRequiringEncryption(destinationPolicy)
            ? ValueEnvelope.Withheld(
                value.Type,
                WithheldValue.PolicyRequiresEncryption(),
                destinationPolicy.RequiresEncryption ? destinationPolicy : value.Policy)
            : null;
        return withheld is not null;
    }
}
