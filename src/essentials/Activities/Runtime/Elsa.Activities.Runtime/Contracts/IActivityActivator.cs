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
/// <remarks>
/// <paramref name="WorkflowExecutionId"/> and <paramref name="ActivityExecutionId"/> are adjacent strings, so the compiler
/// does not catch them transposed. A transposition fails open on masking: the values are registered under the workflow
/// execution id, the work handler masks under the activity execution id, finds nothing registered, and records the
/// failure text unmasked. Construct the request with named arguments for both.
/// </remarks>
public sealed record ActivityActivationRequest(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null,
    RuntimeActivityDescriptor? Descriptor = null);
