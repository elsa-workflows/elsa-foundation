using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Contracts;

public interface IActivityActivator
{
    ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One activation: the pinned contract and committed input snapshot to hydrate, for the attempt being made. The
/// <paramref name="WorkflowExecutionId"/> names the executing instance, whose recorded tenant activation checks
/// before it resolves a secret-bound input. The <paramref name="ActivityExecutionId"/> names the activity execution
/// being activated, under which activation registers each value it resolves from a secret with
/// <see cref="Elsa.Workflows.Runtime.Core.Contracts.IRuntimeSecretMask"/>.
/// </summary>
public sealed record ActivityActivationRequest(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null,
    RuntimeActivityDescriptor? Descriptor = null);
