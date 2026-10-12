using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares a CLR activity's replay/side-effect profile (ADR 0032 R1). Absence is equivalent to
/// <see cref="SideEffectProfile.External"/> — the fail-safe default that keeps the mandatory pre-activation
/// durability boundary. Apply <c>[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]</c> only to an
/// activity that is pure/deterministic between checkpoint boundaries (in-workflow routing, compute, variable
/// transforms), so its attempt-claim checkpoint may be deferred under a coalescing cadence. The declared value
/// is folded into the pinned, fingerprinted <see cref="ActivityContract"/> at publish time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivitySideEffectProfileAttribute(SideEffectProfile profile) : Attribute
{
    public SideEffectProfile Profile { get; } = Enum.IsDefined(profile)
        ? profile
        : throw new ArgumentOutOfRangeException(nameof(profile), profile, "Activity side-effect profile is not defined.");
}
