using Elsa.Primitives.Models;

namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares how a CLR activity's variable writes on a stale base are handled (spec 1221 FR-012, ADR 0092 decision 6).
/// Absence is <see cref="LostUpdateMode.Default"/>: a lost update applies, the last write wins, and a lost-update warning
/// records both writers and the overwritten value. Apply <c>[ActivityLostUpdateMode(LostUpdateMode.Fault)]</c> to an
/// activity whose read-modify-write must be strict, such as a counter or a balance: a real lost update then faults the
/// step through the incident path. The publish compiler pins the declared mode in the executable node; an author can
/// also opt a single node in.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivityLostUpdateModeAttribute(LostUpdateMode mode) : Attribute
{
    public LostUpdateMode Mode { get; } = Enum.IsDefined(mode)
        ? mode
        : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Lost-update mode is not defined.");
}
