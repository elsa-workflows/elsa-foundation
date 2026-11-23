using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workbench.Acceptance;

// Compiled only by ElsaStepStateAcceptanceProbe=true. These are ordinary native activity bodies, not HTTP fixtures
// or substituted services: authoring, compilation, activation, checkpointing and inspection remain the source host's.
[Activity, AccessesWorkflowState]
public sealed class StepStateAcceptanceWriter : Activity<ActivityUnit>
{
    [ActivityInput(DefaultValue = "complete")] public string Mode { get; set; } = "complete";
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Mode == "clear")
        {
            if (context.WorkflowState.CorrelationId != "order-42" || context.WorkflowState.InstanceName != "Ready for review")
                throw new InvalidOperationException("Clearing requires a previously accepted identity.");
            context.WorkflowState.CorrelationId = null;
            context.WorkflowState.InstanceName = " ";
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
        context.WorkflowState.SetOutput("Count", JsonSerializer.SerializeToElement(42));
        context.WorkflowState.CorrelationId = "order-42";
        context.WorkflowState.InstanceName = "Ready for review";
        return ValueTask.FromResult(Mode switch
        {
            "cancel" => ActivityTransition.Cancel<ActivityUnit>("Review cancelled"),
            "fault" => ActivityTransition.Fault<ActivityUnit>(new ActivityFault("test", "Review failed")),
            _ => ActivityTransition.Complete(ActivityUnit.Value)
        });
    }
}

[Activity, AccessesWorkflowState]
public sealed class StepStateAcceptanceProtectedWriter : Activity<ActivityUnit>
{
    [ActivityInput(IsSensitive = true)] public string Secret { get; set; } = string.Empty;
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.WorkflowState.SetOutput("ProtectedAttempt", JsonSerializer.SerializeToElement(Secret));
        context.WorkflowState.CorrelationId = Secret;
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
