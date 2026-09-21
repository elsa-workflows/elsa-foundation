using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Contracts;

public interface IActivityActivator
{
    ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityActivationRequest(
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null,
    RuntimeActivityDescriptor? Descriptor = null);
