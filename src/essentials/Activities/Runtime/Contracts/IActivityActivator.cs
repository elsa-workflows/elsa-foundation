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
/// before it resolves a secret-bound input.
/// </summary>
public sealed record ActivityActivationRequest(
    string WorkflowExecutionId,
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null,
    RuntimeActivityDescriptor? Descriptor = null);
