using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Test-only real CLR leaf, byte-identical in the old capture and upgraded consumer.</summary>
public sealed class LegacyCheckpointOutcomeWaitActivity : StatefulTriggerActivity<ActivityUnit, LegacyCheckpointOutcomeWaitState, LegacyCheckpointOutcomeTrigger>
{
    public const string ResumeTargetId = "resume-target:legacy-outcome";
    protected override ValueTask<ActivityTransition<ActivityUnit, LegacyCheckpointOutcomeWaitState>> ExecuteAsync(ActivityStartContext<LegacyCheckpointOutcomeTrigger> context) =>
        ValueTask.FromResult(Suspend(new LegacyCheckpointOutcomeWaitState("legacy-outcome"),
            [new ActivityTriggerRegistration<LegacyCheckpointOutcomeTrigger>(ResumeTargetId, "test.legacy-outcome", "test.legacy-outcome")]));

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<ActivityUnit, LegacyCheckpointOutcomeWaitState>> ResumeAsync(ActivityResumeContext<LegacyCheckpointOutcomeWaitState, LegacyCheckpointOutcomeTrigger> context) =>
        ValueTask.FromResult(context.Trigger.Outcome switch
        {
            "complete" => Complete(new ActivityUnit()),
            "fault" => Fault(new ActivityFault("LEGACY-OUTCOME-FAULT", "Actual legacy outcome wait faulted.")),
            "cancel" => Cancel("Actual legacy outcome wait canceled."),
            _ => throw new InvalidOperationException("Unsupported test outcome.")
        });
}

public sealed record LegacyCheckpointOutcomeWaitState(string Name);
public sealed record LegacyCheckpointOutcomeTrigger([property: System.Text.Json.Serialization.JsonPropertyName("outcome")] string Outcome);
