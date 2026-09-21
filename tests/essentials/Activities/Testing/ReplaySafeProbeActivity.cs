using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Testing;

/// <summary>
/// A ReplaySafe-marked twin of <see cref="ProbeActivity"/> for spec-123 fusion tests. Behaviorally identical to
/// <see cref="ProbeActivity"/> — records that it ran and emits a fixed outcome — but its pinned contract declares
/// <see cref="SideEffectProfile.ReplaySafe"/>, so a fusion shape whose leaves are these nodes is eligible for the
/// D1 fused schedule→start→invoke pass. <see cref="ProbeActivity"/> itself stays unmarked (⇒ External) so the
/// existing External attempt/poison guardrail suites are undisturbed.
/// </summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeProbeActivity : Activity<ActivityUnit>
{
    /// <summary>The activity type discriminator used for ReplaySafe probe nodes.</summary>
    public const string ReplaySafeProbeActivityType = "test/probe-replay-safe";

    [ActivityInput]
    public IReadOnlyCollection<string> Outcomes { get; set; } = [ActivityOutcomes.Done];

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, Outcomes.Single()));
}
