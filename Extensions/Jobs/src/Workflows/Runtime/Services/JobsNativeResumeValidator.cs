using Elsa.Events.Core.Contracts;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Sequential read-only validation before the native trigger claim and activity activation.</summary>
internal sealed class JobsNativeResumeValidator(IJobQueries jobs, IPersistenceAccessContextAccessor access)
    : IEventHandler<ActivityResumeValidating>
{
    public async Task Handle(ActivityResumeValidating context, CancellationToken cancellationToken)
    {
        if (context.State.Execution.ActivityType != JobsNativeBoundary.ActivityType) return;
        cancellationToken.ThrowIfCancellationRequested();
        JobNativeAdmission admission;
        try
        {
            // This is engine-owned retained authority, before any trigger claim. Losing or
            // corrupting it must defer the queued resume rather than ACK/poison that work.
            admission = JobsNativeBoundary.ReadAdmission(context.State);
            access.Current.EnsureScope(new(admission.Address.Scope.PersistencePartition));
            if (context.Node.ActivityType != JobsNativeBoundary.ActivityType || context.Node.ExecutableNodeId != admission.Address.NodeId)
                throw JobsNativeBoundary.Invalid();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            throw new RuntimeSchedulerWorkUnavailableException("jobs.native.admission-unavailable",
                "The retained Jobs admission authority cannot currently be verified; the resume remains owed.", error);
        }
        var address = admission.Address; var payload = context.Payload;
        // Deliberate malformed/conflicting incoming payloads retain their existing refusal policy.
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, address.Executable) ||
            payload.ActivityExecutionId != address.InvocationId || payload.ExecutableNodeId != address.NodeId ||
            payload.BookmarkId != address.BookmarkId || payload.ResumeTargetId != address.ResumeTargetId ||
            payload.StimulusType != address.StimulusType || payload.StimulusHash != address.StimulusHash ||
            payload.Input is not { } input || payload.TriggerDelivery is not { } delivery ||
            delivery.ProviderId != JobsNativeBoundary.ProviderId || delivery.PayloadType != JobsNativeBoundary.CompletionType) throw JobsNativeBoundary.Invalid();
        var completion = JobsNativeBoundary.Read<RunJobCompletion>(input);
        if (delivery.DeduplicationKey != JobsNativeBoundary.DeduplicationKey(address, JobsNativeBoundary.DeliveryId(address, completion)))
            throw JobsNativeBoundary.Invalid();
        _ = await new JobsNativeResultValidator(jobs).ValidateAsync(admission, completion, cancellationToken);
    }
}
