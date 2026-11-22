using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// A leaf that calls the shared <see cref="Participant"/> with its operation key. A forward step applies an effect; a
/// compensation (an activity with a target operation key) undoes its target. It faults when the participant rejects
/// its node or is unavailable, reports <c>outcome-unknown</c> when the response is lost, and stops the host after the
/// effect when the test asks for it.
/// </summary>
public sealed class ParticipantActivity(Participant participant, HostKillSwitch killSwitch) : Activity<ActivityUnit>
{
    public const string RejectionCode = "order.rejected";
    public const string UnavailableCode = "participant.unavailable";
    public const string TimeoutCode = "participant.timeout";
    public const string RefusedCode = "participant.refused";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (participant.Rejects(context.ExecutableNodeId))
            return Fault(new ActivityFault(RejectionCode, $"'{context.ExecutableNodeId}' was rejected."));

        if (participant.StopsBefore(context.ExecutableNodeId))
        {
            killSwitch.Kill();
            return Done();
        }

        if (participant.FailsNow(context.ExecutableNodeId, context.InvocationId, context.OperationKey))
            return Fault(new ActivityFault(UnavailableCode, $"'{context.ExecutableNodeId}' is unavailable."));

        if (context.TargetOperationKey is { } target)
        {
            if (participant.Undo(context.ExecutableNodeId, context.InvocationId, context.OperationKey, target))
                killSwitch.Kill();
            return Done();
        }

        var result = participant.Apply(context.ExecutableNodeId, context.InvocationId, context.OperationKey, out var stopHost);
        if (stopHost)
            killSwitch.Kill();

        return result switch
        {
            ForwardResult.ResponseLost => Fault(new ActivityFault(TimeoutCode, $"'{context.ExecutableNodeId}' got no response.", category: CompensationScope.OutcomeUnknownCategory)),
            ForwardResult.Refused => Fault(new ActivityFault(RefusedCode, $"'{context.ExecutableNodeId}' was refused: its operation was already compensated.")),
            _ => Done()
        };
    }

    private static ValueTask<ActivityTransition<ActivityUnit>> Done() => ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

    private static ValueTask<ActivityTransition<ActivityUnit>> Fault(ActivityFault fault) => ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(fault));
}
