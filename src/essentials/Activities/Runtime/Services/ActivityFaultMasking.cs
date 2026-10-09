using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// One scheduler work handler's masking of the failure text of the activity execution it handles (spec 188, FR-012):
/// the values that activation resolved from secrets for that execution, as <see cref="IRuntimeSecretMask"/> holds them,
/// are masked in the exception and the returned <see cref="ActivityFault"/> the handler's fault boundaries record, and
/// in the disposal failures its cancellation arms report. Disposing it releases the execution's values from the mask.
/// </summary>
/// <remarks>
/// <para>
/// The handler disposes it once it has recorded the outcome, not when the activation lease is disposed: every fault
/// boundary disposes the lease before it records the fault, and the activator disposes the lease of an activation that
/// failed before the handler sees the failure, so the values must outlive the lease to mask that text.
/// </para>
/// <para>
/// A cancellation is never masked: the arms that take the activation's cancellation rethrow it unchanged, so it stays a
/// cancellation and is never recorded as a fault.
/// </para>
/// </remarks>
public sealed class ActivityFaultMasking(IRuntimeSecretMask mask, string activityExecutionId) : IDisposable
{
    private static readonly ActivityActivationFailureHandler ActivationFailures = new();

    /// <summary>The masking of <paramref name="activityExecutionId"/>'s failure text with the scope's <see cref="IRuntimeSecretMask"/>.</summary>
    public static ActivityFaultMasking For(IServiceProvider services, string activityExecutionId) =>
        new(services.GetRequiredService<IRuntimeSecretMask>(), activityExecutionId);

    /// <summary>
    /// Returns <paramref name="exception"/> itself while no value is registered for the execution. Otherwise returns a
    /// <see cref="SecretMaskedException"/> that stands in for it, with every registered value masked in its message,
    /// stack trace and inner exceptions and its classification kept, whether or not the text contains a value: an
    /// exception can carry a value in places the mask cannot see, so none of it is handed on. An exception that
    /// <see cref="ActivityActivationFailureHandler"/> classifies as an activation failure (a missing storage driver,
    /// activity consumer or secret resolver) is handed on as it is: its message is built from deployment identifiers,
    /// never from an input's value, and replacing it would turn a deployment problem that parks the activity into a fault.
    /// </summary>
    public Exception Mask(Exception exception) =>
        !mask.HasRegistrations(activityExecutionId) || ActivationFailures.Classify(exception) is not null
            ? exception
            : new SecretMaskedException(exception, text => mask.Mask(activityExecutionId, text));

    /// <summary>
    /// Returns <paramref name="fault"/> with every value registered for the execution masked in its message, before the
    /// fault is projected onto the durable record and the incident. Its code, classification and retryability are kept.
    /// </summary>
    public ActivityFault Mask(ActivityFault fault) =>
        mask.HasRegistrations(activityExecutionId)
            ? new ActivityFault(fault.Code, mask.Mask(activityExecutionId, fault.Message), fault.IsRetryable, fault.Category, fault.FaultType)
            : fault;

    /// <summary>Releases the execution's values from the mask.</summary>
    public void Dispose() => mask.Release(activityExecutionId);
}
