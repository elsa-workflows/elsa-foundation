using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Reads, at design time, the <see cref="SideEffectProfile"/> the executable compiler would compile for an activity
/// version (spec 261 FR-513), so a Draft validator can see it without publishing. One implementation, owned by
/// Publishing, applies the compiler's own rule, so publication and a validator cannot disagree.
/// </summary>
public interface IActivitySideEffectProfileReader
{
    /// <summary>
    /// The profile the executable compiler would compile for <paramref name="activityVersionId"/>: a CLR activity's
    /// declared profile (<see cref="SideEffectProfile.External"/> when it declares none), and
    /// <see cref="SideEffectProfile.External"/> for any other kind of activity, such as a reusable activity. Null when
    /// the version does not exist or its CLR type is not loaded.
    /// </summary>
    ValueTask<SideEffectProfile?> ReadAsync(string activityVersionId, CancellationToken cancellationToken = default);
}
