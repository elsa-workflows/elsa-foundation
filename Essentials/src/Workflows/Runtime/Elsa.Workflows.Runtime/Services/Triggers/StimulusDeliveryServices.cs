using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Scoped source-owned routing dependencies; no delivery state or independent authority is stored here.</summary>
internal sealed class StimulusDeliveryServices(
    IStimulusDeliveryStore sources,
    IRuntimePostCommitOutboxStore outbox,
    IWorkflowExecutableStore material,
    IWorkflowExecutableSourceReferenceStore references,
    IWorkflowExecutionStateStore executions,
    IActivityExecutionStateStore activities,
    IBookmarkStateStore bookmarks,
    IRuntimeCheckpointCommitStore checkpoints,
    IEnumerable<IStimulusDeliveryIntentAdapter> adapters,
    TimeProvider clock,
    IPersistenceAccessContextAccessor? access = null,
    CoalescingInner<IWorkflowExecutionStateStore>? physicalExecutions = null,
    CoalescingInner<IActivityExecutionStateStore>? physicalActivities = null,
    CoalescingInner<IRuntimePostCommitOutboxStore>? physicalOutbox = null)
{
    private readonly StimulusDeliveryRequestProjector _requests = new(adapters);
    internal IStimulusDeliveryStore Sources => sources;
    internal IWorkflowExecutableStore Material => material;
    internal IWorkflowExecutionStateStore Executions => executions;

    internal void RequireBoundary(PostCommitDeliveryContext source)
    {
        var rawExecutions = executions.GetType() == typeof(CoalescingWorkflowExecutionStateStore) ? physicalExecutions?.Value : executions;
        var rawActivities = activities.GetType() == typeof(CoalescingActivityExecutionStateStore) ? physicalActivities?.Value : activities;
        var rawOutbox = outbox.GetType() == typeof(CoalescingRuntimePostCommitOutboxStore) ? physicalOutbox?.Value : outbox;
        (access?.Current ?? throw new NotSupportedException("Source-owned stimulus delivery requires explicit persistence access.")).EnsureScope(source.Scope);
        if (rawExecutions is null || rawActivities is null || rawOutbox is null ||
            !sources.CanRetainExecutables(rawOutbox, material, references, rawExecutions) ||
            !checkpoints.CanCommitExactBookmarkResume(bookmarks, rawExecutions, rawActivities))
            throw new NotSupportedException("Source-owned stimulus delivery requires coherent physical source, retention and exact resume stores.");
    }

    internal StimulusDispatchRequest Request(PostCommitDeliveryContext source) => _requests.Read(source);

    internal async ValueTask<StimulusDeliverySnapshot> SelectAsync(PostCommitDeliveryContext source, StimulusDispatchRequest request,
        IGlobalBookmarkStimulusLookup lookup, IWorkflowTriggerBindingStore bindings, CancellationToken cancellationToken)
    {
        var publisher = await executions.FindAsync(source.Intent.WorkflowExecutionId, cancellationToken)
            ?? throw new IOException("The authoritative stimulus publisher is unavailable.");
        access!.Current.EnsureTenantScope(publisher.TenantId);
        var resumes = new List<StimulusResumeRecipient>();
        if (request.Mode != StimulusRoutingMode.StartOnly)
        {
            var waiting = await lookup.FindWaitingAsync(new(request.StimulusType, request.StimulusHash, clock.GetUtcNow(), request.CorrelationId), cancellationToken);
            foreach (var candidate in waiting.Matches)
            {
                if (request.TargetWorkflowExecutionId is { } target && candidate.WorkflowExecutionId != target) continue;
                var observation = await bookmarks.ObserveAsync(candidate.WorkflowExecutionId, candidate.BookmarkId, cancellationToken);
                if (observation is null) continue;
                var wait = observation.State;
                if (wait.StimulusType != request.StimulusType || wait.StimulusHash != request.StimulusHash ||
                    !GlobalBookmarkStimulusLookupResult.CorrelationMatches(wait, request.CorrelationId)) continue;
                var execution = await executions.FindAsync(wait.WorkflowExecutionId, cancellationToken)
                    ?? throw new IOException("A discovered wait's owning execution is unavailable.");
                // Native material can be shared. An explicitly foreign tenant is never a recipient of this physical scope.
                if (execution.TenantId is { } tenant && tenant != source.Scope.Value) continue;
                if (execution.RunKind == WorkflowRunKind.TestRun || execution.TestScope is not null) continue;
                var activity = await activities.FindAsync(wait.WorkflowExecutionId, wait.ActivityExecutionId, cancellationToken)
                    ?? throw new IOException("A discovered wait's owning activity is unavailable.");
                var captured = StimulusResumeRecipient.From(observation, execution, source.Scope);
                if (captured.MatchesAdmission(observation, execution, activity, source.Scope, clock.GetUtcNow())) resumes.Add(captured);
            }
        }
        // Every wait is captured before any start binding is dispatched or even captured.
        var starts = new List<StimulusStartRecipient>();
        if (request.Mode != StimulusRoutingMode.ResumeOnly)
        {
            var selected = await bindings.ListAllByStimulusAsync(request.StimulusType, request.StimulusHash, cancellationToken);
            var resolver = new WorkflowExecutableStartSourceResolver(references, clock);
            foreach (var binding in selected.OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ThenBy(item => item.TriggerBindingId, StringComparer.Ordinal))
            {
                var executable = await material.FindAsync(binding.ArtifactId, cancellationToken)
                    ?? throw new IOException("A selected stimulus executable is unavailable.");
                starts.Add(await resolver.CaptureInScopeAsync(binding, executable.Identity, source.Scope, cancellationToken));
            }
        }
        return new(StimulusDeliverySnapshot.CurrentVersion, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From(publisher, source.Scope), clock.GetUtcNow(), request.StimulusType, request.StimulusHash, starts, resumes);
    }

    internal static void ValidateWinner(PostCommitDeliveryContext source, StimulusDispatchRequest request)
    {
        var snapshot = source.Claim.Item.RecipientSnapshot ?? throw new InvalidDataException("The source has no accepted complete cohort.");
        if (snapshot.RetentionAcceptanceVersion != StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion ||
            snapshot.StimulusType != request.StimulusType || snapshot.StimulusHash != request.StimulusHash ||
            request.Mode == StimulusRoutingMode.ResumeOnly && snapshot.Starts.Count != 0 ||
            request.Mode == StimulusRoutingMode.StartOnly && snapshot.Resumes.Count != 0 ||
            snapshot.Starts.Any(entry => !entry.SourceProvenanceCaptured) ||
            snapshot.Resumes.Any(entry => entry.RegistrationCapture is not { } capture ||
                capture.Scope.PersistenceScope != source.Scope || capture.Scope.TenantId is { } tenant && tenant != source.Scope.Value ||
                capture.Scope.RunKind == WorkflowRunKind.TestRun || capture.Scope.TestScope is not null ||
                request.CorrelationId is { } correlation && (!capture.Metadata.TryGetValue(RuntimeMetadataKeys.CorrelationId, out var retained) || retained != correlation) ||
                request.TargetWorkflowExecutionId is { } target && entry.WorkflowExecutionId != target))
            throw new InvalidDataException("The accepted cohort differs from its canonical routing input or lacks complete recipient captures.");
    }

    internal async ValueTask<SavedBookmarkResume?> PrepareResumeAsync(PostCommitDeliveryContext supplied, string executionId,
        string bookmarkId, CancellationToken cancellationToken)
    {
        RequireBoundary(supplied);
        var source = await sources.ReadSourceAsync(supplied, cancellationToken); var request = Request(source); ValidateWinner(source, request);
        var expected = source.Claim.Item.RecipientSnapshot!.Resumes.SingleOrDefault(entry => entry.WorkflowExecutionId == executionId && entry.BookmarkId == bookmarkId)
            ?? throw new InvalidDataException("The exact saved wait is absent from its source cohort.");
        var observed = await bookmarks.ObserveAsync(executionId, bookmarkId, cancellationToken);
        if (observed is null) return null;
        var execution = await executions.FindAsync(executionId, cancellationToken)
            ?? throw new IOException("The saved wait's owning execution is unavailable.");
        var activity = await activities.FindAsync(executionId, expected.ActivityExecutionId, cancellationToken)
            ?? throw new IOException("The saved wait's owning activity is unavailable.");
        access!.Current.EnsureTenantScope(execution.TenantId);
        if (!expected.MatchesAdmission(observed, execution, activity, source.Scope, clock.GetUtcNow())) return null;
        var resume = new BookmarkResumeDispatchRequest(executionId, request.StimulusType, request.StimulusHash, request.Input,
            $"{request.IdempotencyKey}:resume:{executionId}", request.RequestedBy, request.BuildDispatchMetadata(), request.PayloadType,
            request.ProviderId, bookmarkId, null);
        return new(source, resume, expected, observed.State, execution);
    }

}

internal sealed record SavedBookmarkResume(PostCommitDeliveryContext Source, BookmarkResumeDispatchRequest Request,
    StimulusResumeRecipient Expected, BookmarkState Bookmark, WorkflowExecutionState Workflow);
