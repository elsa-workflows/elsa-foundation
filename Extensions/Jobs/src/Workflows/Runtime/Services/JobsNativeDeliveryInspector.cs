using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed record JobsNativeInspection(JobDeliveryProof? Proof, BookmarkState? Bookmark);

/// <summary>Verifies native final-attempt linkage; model-root consumption stays with its owning adapter.</summary>
internal sealed class JobsNativeDeliveryInspector(IWorkflowExecutionStateStore workflows, IWorkflowExecutableStore executables,
    IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks, IPersistenceAccessContextAccessor access,
    IJobQueries jobs, IEnumerable<IJobWorkflowAssociationSource> associations, IPersistenceOperationScopeFactory scopes,
    IExternalPayloadStore? external = null)
    : IJobNativeDeliveryInspector
{
    public async ValueTask<JobNativeDeliveryEvidence?> InspectWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default)
    {
        try
        {
            // A bridge can be evaluating a different model in a coalescing scope. Evidence comes
            // from a fresh partition-bound scope, never that scope's uncommitted continuation view.
            await using var scope = await scopes.CreateAsync(new(worker.Partition?.Value ?? throw JobsNativeBoundary.Invalid()), cancellationToken);
            return await scope.ServiceProvider.GetRequiredService<JobsNativeDeliveryInspector>().InspectWorkerInScopeAsync(worker, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JobException) { throw; }
        catch (Exception error) { throw new JobException("jobs.native.evidence-unavailable", "The exact native Jobs delivery evidence is not currently available.", error); }
    }

    private async ValueTask<JobNativeDeliveryEvidence?> InspectWorkerInScopeAsync(WorkflowExecutionState worker, CancellationToken cancellationToken)
    {
        var root = await FindRootInScopeAsync(worker, cancellationToken);
        return root is null ? null : await EvidenceAsync(worker, root, cancellationToken);
    }

    internal async ValueTask<ActivityExecutionState?> FindRootInScopeAsync(WorkflowExecutionState worker, CancellationToken cancellationToken)
    {
        access.Current.EnsureScope(new(worker.Partition?.Value ?? throw JobsNativeBoundary.Invalid()));
        var retained = await workflows.FindAsync(worker.WorkflowExecutionId, cancellationToken) ?? throw JobsNativeBoundary.Invalid();
        if (retained.TenantId != worker.TenantId || retained.Partition != worker.Partition || retained.Status != worker.Status ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(retained.PinnedExecutable, worker.PinnedExecutable) ||
            (retained.ModelWorkOrigin is null) != (worker.ModelWorkOrigin is null) ||
            retained.ModelWorkOrigin is { } origin && !origin.IsEquivalentTo(worker.ModelWorkOrigin)) throw JobsNativeBoundary.Invalid();
        var executable = await executables.FindAsync(worker.PinnedExecutable.ArtifactId, cancellationToken) ?? throw JobsNativeBoundary.Invalid();
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, worker.PinnedExecutable)) throw JobsNativeBoundary.Invalid();
        if (executable.RootActivity.ActivityType != JobsNativeBoundary.ActivityType) return null;
        ActivityExecutionState? root = null; string? after = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(worker.WorkflowExecutionId, continuationToken: after), cancellationToken);
            foreach (var candidate in page.Items.Where(candidate => candidate.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId &&
                         candidate.ParentActivityExecutionId is null && candidate.SupersededByActivityExecutionId is null))
            {
                if (root is not null) throw JobsNativeBoundary.Invalid(); root = candidate;
            }
            after = page.NextContinuationToken;
            if (after is not null && !seen.Add(after)) throw JobsNativeBoundary.Invalid();
        } while (after is not null);
        if (root is null) throw JobsNativeBoundary.Invalid();
        if (executable.RootActivity.ActivityContract is not { } contract || root.ContractIdentity is not { } pinned ||
            root.Execution.ActivityTypeVersion != executable.RootActivity.ActivityTypeVersion || pinned.ActivityTypeKey != contract.ActivityTypeKey ||
            pinned.ContractVersion != contract.ContractVersion || pinned.SchemaFingerprint != contract.SchemaFingerprint) throw JobsNativeBoundary.Invalid();
        return root;
    }

    internal async ValueTask<JobsNativeInspection> InspectAsync(JobSnapshot snapshot, JobNativeAddress address,
        RunJobCompletion? completion, CancellationToken cancellationToken)
    {
        var result = snapshot.Result ?? throw JobsNativeBoundary.Invalid();
        access.Current.EnsureScope(new(address.Scope.PersistencePartition));
        var parent = await workflows.FindAsync(address.WorkflowExecutionId, cancellationToken);
        if (address.Owner.Origin is { } origin)
        {
            var source = new JobsNativeOwnerResolver(associations).Select(origin);
            var owner = await source.InspectAsync(address.Owner, null, cancellationToken);
            if (owner.Disposition == JobWorkflowOwnerDisposition.ProvenTerminal)
                return new(OwnerProof(owner, result, terminal: true), null);
        }
        if (parent is null) return new(null, null);
        MatchParent(parent, address);
        if (address.Owner.Origin is null && parent.Status.IsTerminal())
        {
            if (parent.CompletedAt is not { } ended) throw JobsNativeBoundary.Invalid();
            return new(new(JobDeliveryProofKind.ParentProvenTerminal,
                "jobs-parent-" + JobsNativeBoundary.Hash(new { address.Scope, parent.WorkflowExecutionId, parent.PinnedExecutable, parent.Status, Ended = ended }),
                result.PayloadSha256), null);
        }
        var activity = await activities.FindAsync(address.WorkflowExecutionId, address.InvocationId, cancellationToken);
        if (activity is null) return new(null, null);
        var admission = JobsNativeBoundary.ReadAdmission(activity);
        if (admission.RequestSha256 != snapshot.RequestSha256 || !JobsNativeBoundary.Equivalent(admission.Request, snapshot.Request) ||
            !JobsNativeBoundary.Equivalent(admission.Address, address)) throw JobsNativeBoundary.Invalid();
        if (activity.Status == ActivityExecutionStatus.Suspended)
        {
            if (activity.TriggerRegistrations is not { Count: 1 } registrations || registrations.Single().RegistrationId != address.RegistrationId ||
                !activity.BookmarkIds.Contains(address.BookmarkId, StringComparer.Ordinal) ||
                activity.TriggerDeliveries?.Any(receipt => receipt.RegistrationId == address.RegistrationId && receipt.Status == ActivityTriggerDeliveryStatus.Consumed) == true)
                return new(null, null);
            var bookmark = await bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId, cancellationToken);
            if (bookmark is not null) JobsNativeBoundary.EnsureBookmark(address, bookmark);
            return new(null, bookmark);
        }
        var evidence = await EvidenceAsync(parent, activity, cancellationToken);
        if (evidence is null || completion is null || !JobsNativeBoundary.Equivalent(evidence.Completion, completion) ||
            !JobsNativeBoundary.Equivalent(evidence.Result, result)) return new(null, null);
        if (address.Owner.Origin is { } modelOrigin)
        {
            var owner = await new JobsNativeOwnerResolver(associations).Select(modelOrigin).InspectAsync(address.Owner, evidence, cancellationToken);
            return owner.Disposition switch
            {
                JobWorkflowOwnerDisposition.ProvenTerminal => new(OwnerProof(owner, result, terminal: true), null),
                JobWorkflowOwnerDisposition.AcceptedInput => new(OwnerProof(owner, result, terminal: false), null),
                JobWorkflowOwnerDisposition.NotConsumed => new(null, null),
                _ => throw JobsNativeBoundary.Invalid()
            };
        }
        // A cancelled activity alone does not prove its still-live native parent observed the result.
        return evidence.NativeStatus == ActivityExecutionStatus.Cancelled ? new(null, null)
            : new(new(JobDeliveryProofKind.AlreadyConsumed, evidence.NativeAttemptId, result.PayloadSha256), null);
    }

    private async ValueTask<JobNativeDeliveryEvidence?> EvidenceAsync(WorkflowExecutionState parent, ActivityExecutionState activity, CancellationToken cancellationToken)
    {
        if (activity.Execution.ActivityType != JobsNativeBoundary.ActivityType) return null;
        if (!activity.Metadata.TryGetValue(JobsNativeBoundary.DeliveryMetadataKey, out var serialized))
            return activity.Status == ActivityExecutionStatus.Faulted && PreAdmissionFailure(parent, activity) ? null
                : activity.Status == ActivityExecutionStatus.Cancelled && NativeOwnerCancellation(parent, activity) ? null
                : activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled ? throw JobsNativeBoundary.Invalid() : null;
        var admission = JobsNativeBoundary.ReadAdmission(activity); var address = admission.Address; MatchParent(parent, address);
        var binding = JobsNativeBoundary.ReadString<JobNativeDeliveryBinding>(serialized);
        if (activity.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled)) return null;
        if (binding.FormatVersion != 1 || binding.Completion is null || binding.Contract is null || binding.RegistrationId != address.RegistrationId ||
            !JobsNativeBoundary.Equivalent(binding.Contract, activity.ContractIdentity) ||
            binding.DeduplicationKey != JobsNativeBoundary.DeduplicationKey(address, JobsNativeBoundary.DeliveryId(address, binding.Completion)) ||
            activity.TriggerDeliveries is not { Count: > 0 and <= 32 } deliveries || activity.Attempts is not { Count: > 0 and <= 32 } attempts)
            throw JobsNativeBoundary.Invalid();
        var matches = deliveries.Where(receipt => receipt.DeduplicationKey == binding.DeduplicationKey).ToArray();
        if (matches.Length != 1 || matches[0] is not { Status: ActivityTriggerDeliveryStatus.Consumed } receipt ||
            receipt.DeliveryId != binding.DeliveryId || receipt.RegistrationId != binding.RegistrationId ||
            receipt.ProviderId != JobsNativeBoundary.ProviderId || receipt.PayloadType != JobsNativeBoundary.CompletionType ||
            receipt.Payload.Type != JobsNativeBoundary.CompletionType) throw JobsNativeBoundary.Invalid();
        if (receipt.Payload.Presence == ValuePresence.Present)
        {
            var actual = await ValueAsync(receipt.Payload, cancellationToken);
            if (actual is null || !JobsNativeBoundary.Equivalent(JobsNativeBoundary.Read<RunJobCompletion>(actual.Value), binding.Completion)) throw JobsNativeBoundary.Invalid();
        }
        else if (receipt.Payload.Presence != ValuePresence.Absent) throw JobsNativeBoundary.Invalid();
        var ordered = attempts.OrderBy(attempt => attempt.Ordinal).ToArray();
        if (ordered.Select(attempt => attempt.Ordinal).Distinct().Count() != ordered.Length ||
            ordered.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(attempt => attempt.InvocationId != address.InvocationId || attempt.EndedAt is null || attempt.TransitionKind is null)) throw JobsNativeBoundary.Invalid();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i - 1].EndedAt > ordered[i].StartedAt) throw JobsNativeBoundary.Invalid();
        var chain = ordered.Where(attempt => attempt.TriggerDeliveryId == binding.DeliveryId).ToArray();
        var final = ordered[^1];
        if (chain.Length == 0 || chain[^1] != final || final.AttemptId != binding.AttemptId || final.StartedAt < receipt.ReceivedAt ||
            final.EndedAt != activity.CompletedAt || chain.Any(attempt => attempt.Reason != ActivityAttemptReason.Resume) ||
            chain.Take(chain.Length - 1).Any(attempt => attempt.TransitionKind != ActivityTransitionKind.Fault)) throw JobsNativeBoundary.Invalid();
        var result = await new JobsNativeResultValidator(jobs).ValidateAsync(admission, binding.Completion, cancellationToken);
        RunJobResult? value = null; string? marker = null;
        if (activity.Status == ActivityExecutionStatus.Cancelled)
        {
            // An owner may cancel after typed consumption; that physical native outcome is still
            // bound to the checked grant above, never inferred from missing delivery metadata.
            if (final.TransitionKind != ActivityTransitionKind.Cancel || activity.Completion is not null) throw JobsNativeBoundary.Invalid();
        }
        else if (result.Outcome == JobOutcome.Succeeded)
        {
            if (activity.Status != ActivityExecutionStatus.Completed || final.TransitionKind != ActivityTransitionKind.Complete ||
                activity.Completion is not { } completion || activity.Fault is not null || completion.InvocationId != address.InvocationId ||
                completion.AttemptId != final.AttemptId || completion.ContractFingerprint != address.Contract.SchemaFingerprint ||
                completion.OutcomeKey != "Done" || completion.Result.Type != new ValueTypeDescriptor(typeof(RunJobResult).FullName!) ||
                completion.CompletedAt < final.StartedAt || completion.CompletedAt > final.EndedAt) throw JobsNativeBoundary.Invalid();
            var actual = await ValueAsync(completion.Result, cancellationToken) ?? throw JobsNativeBoundary.Invalid();
            value = JobsNativeBoundary.Read<RunJobResult>(actual);
            if (!JobsNativeBoundary.Equivalent(value, JobsNativeBoundary.Result(binding.Completion, result))) throw JobsNativeBoundary.Invalid();
        }
        else if (result.Outcome is JobOutcome.Failed or JobOutcome.TimedOut)
        {
            marker = JobsNativeBoundary.TerminalMarker(binding.Completion);
            if (activity.Status != ActivityExecutionStatus.Faulted || final.TransitionKind != ActivityTransitionKind.Fault || activity.Completion is not null ||
                activity.Fault is not { } fault || fault.Code != (result.Diagnostic?.Code ?? "jobs.job.failed") ||
                fault.ExceptionType != typeof(ActivityFault).FullName || fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) != marker)
                throw JobsNativeBoundary.Invalid();
        }
        else throw JobsNativeBoundary.Invalid();
        return new(address, binding.Completion, result, binding.DeliveryId, final.AttemptId, activity.Status, value, marker);
    }

    private async ValueTask<System.Text.Json.JsonElement?> ValueAsync(ValueEnvelope value, CancellationToken cancellationToken)
    {
        if (value.Presence != ValuePresence.Present) return null;
        if (value.InlineValue is { } json) return json;
        if (value.ExternalReference is not { } reference || external is null) throw JobsNativeBoundary.Invalid();
        return await external.ReadAsync(reference, cancellationToken);
    }

    internal static void MatchParent(WorkflowExecutionState parent, JobNativeAddress address)
    {
        if (parent.WorkflowExecutionId != address.WorkflowExecutionId || parent.Partition?.Value != address.Scope.PersistencePartition ||
            parent.TenantId != address.Scope.TenantId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(parent.PinnedExecutable, address.Executable) ||
            (parent.ModelWorkOrigin is null) != (address.Owner.Origin is null) ||
            parent.ModelWorkOrigin is { } origin && !origin.IsEquivalentTo(address.Owner.Origin)) throw JobsNativeBoundary.Invalid();
    }

    private static JobDeliveryProof OwnerProof(JobWorkflowOwnerObservation owner, JobResult result, bool terminal)
    {
        if (owner.ModelReceiptId is null || !terminal && (owner.ResultId != result.ResultId || owner.ResultSha256 != result.PayloadSha256))
            throw JobsNativeBoundary.Invalid();
        JobsNativeBoundary.Name(owner.ModelReceiptId);
        return new(terminal ? JobDeliveryProofKind.ParentProvenTerminal : JobDeliveryProofKind.AlreadyConsumed, owner.ModelReceiptId, result.PayloadSha256);
    }

    internal static bool PreAdmissionFailure(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (parent.Status != WorkflowExecutionStatus.Faulted || activity.Status != ActivityExecutionStatus.Faulted ||
            activity.PrivateState is not null || activity.Completion is not null || activity.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey) ||
            activity.Metadata.ContainsKey(JobsNativeBoundary.DeliveryMetadataKey) || activity.BookmarkIds.Count != 0 ||
            activity.TriggerRegistrations?.Count > 0 || activity.TriggerDeliveries?.Count > 0 ||
            activity.Attempts is not { Count: > 0 and <= 32 } attempts || activity.ContractIdentity is not { ActivityTypeKey: JobsNativeBoundary.ActivityType } ||
            activity.Fault is not { } fault || fault.Code != JobsNativeBoundary.PreAdmissionFaultCode || fault.ExceptionType != typeof(ActivityFault).FullName)
            return false;
        var ordered = attempts.OrderBy(attempt => attempt.Ordinal).ToArray();
        if (ordered.Select(attempt => attempt.Ordinal).Distinct().Count() != ordered.Length ||
            ordered.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(attempt => attempt.InvocationId != activity.InvocationId || attempt.Reason == ActivityAttemptReason.Resume ||
                attempt.TriggerDeliveryId is not null || attempt.EndedAt is null || attempt.TransitionKind != ActivityTransitionKind.Fault)) return false;
        for (var i = 1; i < ordered.Length; i++) if (ordered[i - 1].EndedAt > ordered[i].StartedAt) return false;
        var final = ordered[^1];
        return activity.CompletedAt == final.EndedAt && parent.CompletedAt is { } ended && activity.CompletedAt <= ended &&
            fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == JobsNativeBoundary.PreAdmissionMarker(parent, activity, final.AttemptId);
    }

    internal static bool NativeOwnerCancellation(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (parent.Status != WorkflowExecutionStatus.Cancelled || activity.Status != ActivityExecutionStatus.Cancelled ||
            activity.Completion is not null || activity.Metadata.ContainsKey(JobsNativeBoundary.DeliveryMetadataKey) ||
            activity.TriggerDeliveries?.Any(receipt => receipt.Status == ActivityTriggerDeliveryStatus.Consumed) == true ||
            activity.Attempts?.Any(attempt => attempt.Reason == ActivityAttemptReason.Resume || attempt.TriggerDeliveryId is not null) == true ||
            !activity.Metadata.TryGetValue(JobsNativeBoundary.OwnerCancellationMetadataKey, out var marker)) return false;
        var admission = JobsNativeBoundary.ReadAdmission(activity);
        return marker == JobsNativeBoundary.OwnerCancellationMarker(admission);
    }
}
