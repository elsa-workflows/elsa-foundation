using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Returns a deliberate typed fault. The runtime commits its normalized fault record and blocking
/// incident without exposing exception control flow to the activity author or host caller.
/// </summary>
public sealed class Fault : Activity<ActivityUnit>
{
    /// <summary>The fault message recorded on the incident. Defaults to a generic message when unset.</summary>
    [ActivityInput(Key = "message")]
    public string? Message { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault(
            "workflow.fault",
            string.IsNullOrWhiteSpace(Message) ? "The workflow faulted." : Message)));
}
