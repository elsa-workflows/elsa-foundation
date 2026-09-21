using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A ReplaySafe-marked leaf that suspends on a bookmark (spec 123 fusion shape (d)). Fusion engages at its
/// <c>ScheduleActivity</c> (ReplaySafe leaf in a burst); the inline invoke stage runs the body which suspends, so the
/// fused span exits mid-way at the suspension commit — which must be byte-identical to the discrete suspend because
/// fusion dispatches the unchanged invoke handler inline. Mirrors <see cref="WaitingActivity"/> but ReplaySafe.
/// </summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeWaitingActivity : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "wait-replay-safe";

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, "TestWaitReplaySafe", "wait:99")]));

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}
