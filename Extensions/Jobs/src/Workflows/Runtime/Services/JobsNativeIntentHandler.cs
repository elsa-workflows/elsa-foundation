using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed class JobsNativeIntentHandler(IJobCommands jobs, IJobQueries queries, IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks, IPersistenceAccessContextAccessor access,
    IEnumerable<IJobWorkflowAssociationSource> associations, JobsNativeSourceVerifier sourceVerifier)
    : IRuntimePostCommitDurableSourceIntentHandler
{
    internal static (JobNativeAdmission Admission, bool Cancel) Read(RuntimePostCommitIntent intent)
    {
        if (intent.Payload is not { } payload || intent.Kind is not (JobsNativeBoundary.StartKind or JobsNativeBoundary.CancelKind))
            throw JobsNativeBoundary.Invalid();
        var native = JobsNativeBoundary.Read<JobNativeIntent>(payload);
        var admission = native.Admission;
        if (native.FormatVersion != 1 || admission is null || admission.Invocation is null || admission.Address is null || !JobsNativeBoundary.Equivalent(admission,
                JobsNativeBoundary.Admission(admission.Invocation, admission.Address, admission.CommittedAt)) ||
            !intent.IsEquivalentTo(JobsNativeBoundary.Intent(admission, intent.Kind == JobsNativeBoundary.CancelKind)))
            throw JobsNativeBoundary.Invalid();
        return (admission, intent.Kind == JobsNativeBoundary.CancelKind);
    }

    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        await sourceVerifier.VerifyAsync(source, cancellationToken);
        await HandleIntentAsync(source.Intent, cancellationToken);
    }

    internal async ValueTask HandleIntentAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        (JobNativeAdmission Admission, bool Cancel) parsed;
        try { parsed = Read(intent); }
        catch (Exception error) when (error is JobException or ArgumentException)
        { throw Failure(PostCommitFailureKind.Permanent, "jobs.native.intent-invalid", error); }
        var admission = parsed.Admission; var address = admission.Address;
        try
        {
            if (access.Current.RequireScope().Value != admission.Invocation.Scope.PersistencePartition) throw JobsNativeBoundary.Invalid();
            if (parsed.Cancel)
            {
                // The explicit committed cancellation source is positive authority even after native
                // state cleanup. This is not a missing-parent observation or a delivery acknowledgement.
                await CancelAsync(admission, intent, cancellationToken); return;
            }
            // Exact protected sender recognition precedes native wait reads. A committed admission
            // or cancellation can outlive that wait; an unknown sender still needs the real wait.
            var originState = await queries.FindOriginAsync(address.Scope, admission.Origin, cancellationToken);
            if (originState is not null)
            {
                JobsNativeBoundary.EnsureOrigin(admission, originState);
                if (originState.Acceptance is not null || originState.Cancelled || originState.Retired) return;
            }
            var parent = await workflows.FindAsync(address.WorkflowExecutionId, cancellationToken) ?? throw Unavailable();
            if (parent.TenantId != address.Scope.TenantId || parent.Partition?.Value != address.Scope.PersistencePartition ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(parent.PinnedExecutable, address.Executable) ||
                (parent.ModelWorkOrigin is null) != (address.Owner.Origin is null) ||
                parent.ModelWorkOrigin is { } origin && !origin.IsEquivalentTo(address.Owner.Origin)) throw JobsNativeBoundary.Invalid();
            if (parent.Status.IsTerminal())
            {
                await CancelAsync(admission, intent, cancellationToken); return;
            }
            if (address.Owner.Origin is { } modelOrigin)
            {
                var owner = await new JobsNativeOwnerResolver(associations).Select(modelOrigin)
                    .InspectAsync(address.Owner, null, cancellationToken);
                if (owner.Disposition == JobWorkflowOwnerDisposition.ProvenTerminal)
                {
                    await CancelAsync(admission, intent, cancellationToken); return;
                }
                if (owner.Disposition != JobWorkflowOwnerDisposition.NotConsumed) throw JobsNativeBoundary.Invalid();
            }
            var activity = await activities.FindAsync(address.WorkflowExecutionId, address.InvocationId, cancellationToken) ?? throw Unavailable();
            if (!JobsNativeBoundary.Equivalent(JobsNativeBoundary.ReadAdmission(activity), admission)) throw JobsNativeBoundary.Invalid();
            if (activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded)
            {
                await CancelAsync(admission, intent, cancellationToken); return;
            }
            // The protected origin recognizes matching lost-ack replay even after its wait retires.
            // Live waits still prove first admission; the store arbitrates a concurrent cancel-before-admit.
            if (activity.Status != ActivityExecutionStatus.Suspended) throw Unavailable();
            if (activity.TriggerRegistrations is not { Count: 1 } || activity.TriggerRegistrations.Single().RegistrationId != address.RegistrationId ||
                !activity.BookmarkIds.Contains(address.BookmarkId, StringComparer.Ordinal)) throw JobsNativeBoundary.Invalid();
            var bookmark = await bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId, cancellationToken) ?? throw Unavailable();
            JobsNativeBoundary.EnsureBookmark(address, bookmark);
            var result = await jobs.AdmitAsync(new(address.Scope, admission.ProposedJobId, admission.ProducerKey,
                admission.RequestSha256, admission.Request, admission.Origin), cancellationToken);
            if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed)
            {
                if (result.Acceptance is null) throw JobsNativeBoundary.Invalid(); return;
            }
            if (result.Status is JobCommandStatus.OriginCancelled or JobCommandStatus.OriginRetired) return;
            if (result.Status == JobCommandStatus.Conflict) throw JobsNativeBoundary.Invalid();
            throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JobException error) when (error.Code == "jobs.native.invalid")
        { throw Failure(PostCommitFailureKind.Permanent, "jobs.native.intent-conflict", error); }
        catch (RuntimePostCommitDeliveryException) { throw; }
        catch (Exception error) { throw Failure(PostCommitFailureKind.Transient, "jobs.native.intent-unavailable", error); }
    }

    private async ValueTask CancelAsync(JobNativeAdmission admission, RuntimePostCommitIntent intent, CancellationToken cancellationToken)
    {
        var operation = new JobOperation("jobs-cancel-" + JobsNativeBoundary.Hash(new { intent.IntentId, admission.Origin }),
            "jobs.native", "Committed native Jobs cancellation or terminal-owner settlement.");
        var result = await jobs.CancelOriginAsync(new(admission.Address.Scope, admission.Origin, admission.RequestSha256, operation), cancellationToken);
        if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed or JobCommandStatus.OriginCancelled or JobCommandStatus.Terminal) return;
        if (result.Status == JobCommandStatus.Conflict) throw JobsNativeBoundary.Invalid(); throw Unavailable();
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() => new("jobs.native.intent-unavailable",
        "The committed Jobs native responsibility remains owed.");
    private static RuntimePostCommitDeliveryException Failure(PostCommitFailureKind kind, string code, Exception error) =>
        new(kind, code, "The committed Jobs native responsibility is not yet confirmed.", error);
}
