using System.Reflection;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Pins the side-effect profile of every shipped activity. A ReplaySafe activity's attempt claim may be
/// deferred, so a crash before the next commit runs its body again. Nothing at runtime notices an activity that should
/// not have been marked; it just commits less. So the ReplaySafe set is a reviewed list that changes in the same commit
/// as the attribute, and every shipped activity not on it stays External.
/// </summary>
public sealed class ActivitySideEffectProfileTests
{
    private static readonly Type[] ReplaySafeActivities =
    [
        typeof(Compensation.Activities.CompensationScope),
        typeof(Do.Activities.Do),
        typeof(Flowchart.Activities.Flowchart),
        typeof(For.Activities.For),
        typeof(ForEach.Activities.ForEach),
        typeof(Http.Activities.WriteHttpResponse),
        typeof(If.Activities.If),
        typeof(Parallel.Activities.Parallel),
        typeof(Primitives.Activities.Break),
        typeof(Primitives.Activities.Fault),
        typeof(Primitives.Activities.Inline),
        typeof(Primitives.Activities.WriteLine),
        typeof(Primitives.Activities.WriteLines),
        typeof(Sequence.Activities.Sequence),
        typeof(Switch.Activities.Switch),
        typeof(While.Activities.While)
    ];

    /// <summary>Verifies that the shipped activities classified as ReplaySafe exactly match the reviewed set.</summary>
    [Fact]
    public void Only_the_reviewed_activities_are_ReplaySafe()
    {
        // Resolved the way the publish compiler pins it: the inherited attribute, or External when there is none.
        var replaySafe = ActivityContractInventory.Activities
            .Where(type => type.GetCustomAttribute<ActivitySideEffectProfileAttribute>(inherit: true)?.Profile == SideEffectProfile.ReplaySafe)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ReplaySafeActivities.Select(type => type.FullName!).Order(StringComparer.Ordinal), replaySafe);
    }
}
