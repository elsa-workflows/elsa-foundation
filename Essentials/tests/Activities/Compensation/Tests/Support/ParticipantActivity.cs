using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// A leaf that calls the shared <see cref="Participant"/> with its invocation id as the idempotency key. It faults
/// when the participant rejects its node, and stops the host after the effect when the test asks for it.
/// </summary>
public sealed class ParticipantActivity(Participant participant, HostKillSwitch killSwitch) : Activity<ActivityUnit>
{
    public const string RejectionCode = "order.rejected";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (participant.Rejects(context.ExecutableNodeId))
            return ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault(RejectionCode, $"'{context.ExecutableNodeId}' was rejected.")));

        if (participant.Apply(context.ExecutableNodeId, context.InvocationId))
            killSwitch.Kill();

        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
