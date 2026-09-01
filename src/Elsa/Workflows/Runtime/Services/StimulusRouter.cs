using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IStimulusRouter"/> (W7). It closes the largest Elsa 3 → Elsa 4 parity gap: routing an
/// external stimulus to workflows with no explicit execution id. On a stimulus it (1) starts a new instance of
/// every published workflow whose trigger index matches (E3-1), and (2) resumes every waiting instance across
/// executions whose bookmark matches (E3-5).
/// </summary>
/// <remarks>
/// <para>
/// Ordering matters: the resume fan-in set is SNAPSHOT before any new instance is started. In-process dispatch
/// is synchronous, so a just-started instance can run to its first bookmark before the router returns; snapshotting
/// first guarantees those fresh bookmarks are not immediately resumed by the same stimulus that started them.
/// </para>
/// <para>
/// Start-path idempotency (Condition A): when the request carries an idempotency key the router consults
/// <see cref="IStimulusStartDeduplicator"/> so a duplicate delivery does not double-start; it also threads the
/// key into the start and resume dispatch envelopes so the agent mailbox dedups redeliveries. When no key is
/// supplied the start path is at-least-once and a duplicate delivery MAY double-start — a deliberately stated
/// limit (see <c>docs/serialization.md</c>).
/// </para>
/// <para>
/// Correlation (Condition B) is a passive threaded value: it scopes the resume fan-in and is stamped as metadata
/// on dispatch envelopes, but the router does not own a correlation subsystem.
/// </para>
/// </remarks>
public sealed class StimulusRouter : IStimulusRouter
{
    private readonly IWorkflowTriggerBindingStore _triggerBindingStore;
    private readonly IGlobalBookmarkStimulusLookup _globalBookmarkStimulusLookup;
    private readonly IWorkflowStartDispatcher _startDispatcher;
    private readonly IBookmarkResumeDispatcher _resumeDispatcher;
    private readonly IStimulusStartDeduplicator _startDeduplicator;
    private readonly TimeProvider _timeProvider;

    public StimulusRouter(
        IWorkflowTriggerBindingStore triggerBindingStore,
        IGlobalBookmarkStimulusLookup globalBookmarkStimulusLookup,
        IWorkflowStartDispatcher startDispatcher,
        IBookmarkResumeDispatcher resumeDispatcher,
        IStimulusStartDeduplicator startDeduplicator)
        : this(triggerBindingStore, globalBookmarkStimulusLookup, startDispatcher, resumeDispatcher, startDeduplicator, TimeProvider.System)
    {
    }

    public StimulusRouter(
        IWorkflowTriggerBindingStore triggerBindingStore,
        IGlobalBookmarkStimulusLookup globalBookmarkStimulusLookup,
        IWorkflowStartDispatcher startDispatcher,
        IBookmarkResumeDispatcher resumeDispatcher,
        IStimulusStartDeduplicator startDeduplicator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(triggerBindingStore);
        ArgumentNullException.ThrowIfNull(globalBookmarkStimulusLookup);
        ArgumentNullException.ThrowIfNull(startDispatcher);
        ArgumentNullException.ThrowIfNull(resumeDispatcher);
        ArgumentNullException.ThrowIfNull(startDeduplicator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _triggerBindingStore = triggerBindingStore;
        _globalBookmarkStimulusLookup = globalBookmarkStimulusLookup;
        _startDispatcher = startDispatcher;
        _resumeDispatcher = resumeDispatcher;
        _startDeduplicator = startDeduplicator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();
        var dispatchMetadata = request.BuildDispatchMetadata();

        // 1. Snapshot the pre-existing fan-in resume set BEFORE starting new instances, so bookmarks created by
        //    just-started (synchronously executed) instances are never resumed by the stimulus that started them.
        var waitingExecutionIds = request.Mode == StimulusRoutingMode.StartOnly
            ? []
            : await SnapshotWaitingExecutionsAsync(request, now, cancellationToken);

        // 2. Start a new instance for every matching published trigger (E3-1). A targeted request is ResumeOnly by
        //    construction, so it is already excluded here — no separate start-suppression rule is needed.
        var starts = request.Mode == StimulusRoutingMode.ResumeOnly
            ? []
            : await StartMatchingTriggersAsync(request, dispatchMetadata, cancellationToken);

        // 3. Resume the snapshot fan-in set (E3-5).
        var resumes = request.Mode == StimulusRoutingMode.StartOnly
            ? []
            : await ResumeWaitingExecutionsAsync(request, waitingExecutionIds, dispatchMetadata, cancellationToken);

        return new StimulusRoutingResult(starts, resumes);
    }

    private async ValueTask<IReadOnlyList<string>> SnapshotWaitingExecutionsAsync(
        StimulusDispatchRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lookup = await _globalBookmarkStimulusLookup.FindWaitingAsync(
            new GlobalBookmarkStimulusLookupRequest(request.StimulusType, request.StimulusHash, now, request.CorrelationId),
            cancellationToken);

        // A targeted (local) publish narrows the fan-in to the one execution that raised it; the lookup still runs
        // so a target with no matching wait resolves to an empty set rather than an unconditional resume attempt.
        return request.TargetWorkflowExecutionId is { } target
            ? lookup.WorkflowExecutionIds.Where(id => StringComparer.Ordinal.Equals(id, target)).ToArray()
            : lookup.WorkflowExecutionIds;
    }

    private async ValueTask<IReadOnlyList<StimulusStartOutcome>> StartMatchingTriggersAsync(
        StimulusDispatchRequest request,
        IReadOnlyDictionary<string, string> dispatchMetadata,
        CancellationToken cancellationToken)
    {
        // Reuse the caller's already-fetched match set when supplied (e.g. the HTTP endpoint middleware fetched it
        // for its ambiguity guard + per-endpoint options), so a request costs one durable trigger read, not two.
        var bindings = request.MatchedTriggerBindings
            ?? await _triggerBindingStore.ListAllByStimulusAsync(
                request.StimulusType,
                request.StimulusHash,
                cancellationToken);

        // Deterministic order so fan-out of starts is stable across providers.
        var ordered = bindings
            .OrderBy(binding => binding.ArtifactId, StringComparer.Ordinal)
            .ThenBy(binding => binding.TriggerBindingId, StringComparer.Ordinal);

        var outcomes = new List<StimulusStartOutcome>();

        foreach (var binding in ordered)
        {
            // Condition A: dedup the start when an idempotency key is supplied. The key is scoped per artifact so a
            // single stimulus that matches two workflows still starts both, but a redelivery starts neither again.
            string? startIdempotencyKey = null;
            if (request.IdempotencyKey is not null)
            {
                startIdempotencyKey = $"{request.IdempotencyKey}:start:{binding.ArtifactId}";
                if (!_startDeduplicator.TryBeginStart(startIdempotencyKey))
                {
                    outcomes.Add(StimulusStartOutcome.SkippedDuplicate(binding.TriggerBindingId, binding.ArtifactId));
                    continue;
                }
            }

            // Spec 089 FR-001: the stimulus payload reaches started instances through the dedicated
            // stimulus-input channel — the start-side counterpart of the resume path's
            // BookmarkResumeDispatchRequest.Input. Never the workflow-inputs bag (collision/spoof-proof).
            // Spec 089 D: the matched binding's executable node id rides its own reserved channel so a
            // mid-flow-capable activity (e.g. HttpEndpoint) can tell whether it is the node that triggered
            // this run — again never the workflow-inputs bag.
            var startRequest = new WorkflowExecutionStartDispatchRequest(
                artifactId: binding.ArtifactId,
                requestedBy: request.RequestedBy,
                idempotencyKey: startIdempotencyKey,
                metadata: dispatchMetadata,
                stimulusInput: request.Input,
                triggerNodeId: binding.ExecutableNodeId,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: binding.ActivationId is null && binding.SlotId is null
                    ? null
                    : new WorkflowExecutableSourceSelection(activationId: binding.ActivationId, slotId: binding.SlotId),
                // Spec 117 D4: forward the matched binding's metadata (e.g. a BPMN start element id) on its own
                // reserved channel so a structural trigger activity can read per-descriptor routing facets. Never
                // the workflow-inputs bag (collision/spoof-proof), mirroring the trigger-node identity above.
                triggerMetadata: binding.Metadata);

            // Forward the request-affine dispatch options (spec 089 FR-019) so an in-process inline drain of this
            // start can build activity execution contexts from the caller's ambient scope. Live reference only —
            // never persisted (see StimulusDispatchRequest.DispatchOptions), dropped by construction across process
            // boundaries. Stimulus-triggered starts are published dispatches (default reference scope, ADR 0040).
            var result = await _startDispatcher.DispatchAsync(startRequest, dispatchOptions: request.DispatchOptions, cancellationToken: cancellationToken);
            outcomes.Add(StimulusStartOutcome.Started(binding.TriggerBindingId, binding.ArtifactId, result.WorkflowExecutionId));
        }

        return outcomes;
    }

    private async ValueTask<IReadOnlyList<StimulusResumeOutcome>> ResumeWaitingExecutionsAsync(
        StimulusDispatchRequest request,
        IReadOnlyList<string> waitingExecutionIds,
        IReadOnlyDictionary<string, string> dispatchMetadata,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<StimulusResumeOutcome>(waitingExecutionIds.Count);

        foreach (var workflowExecutionId in waitingExecutionIds)
        {
            var resumeRequest = new BookmarkResumeDispatchRequest(
                workflowExecutionId: workflowExecutionId,
                stimulusType: request.StimulusType,
                stimulusHash: request.StimulusHash,
                input: request.Input,
                idempotencyKey: request.IdempotencyKey is null ? null : $"{request.IdempotencyKey}:resume:{workflowExecutionId}",
                requestedBy: request.RequestedBy,
                metadata: dispatchMetadata,
                payloadType: request.PayloadType,
                providerId: request.ProviderId);

            // Same request scope serves every outcome of one HTTP request (spec 089 FR-019 / scenario 5.5): a resume
            // driven by a synchronous-mode endpoint gets the caller's ambient services so its subsequent live write
            // lands on the same exchange. Live reference only — never persisted, dropped across process boundaries.
            var result = await _resumeDispatcher.DispatchAsync(resumeRequest, request.DispatchOptions, cancellationToken);
            outcomes.Add(new StimulusResumeOutcome(workflowExecutionId, result.Status, result.Reason));
        }

        return outcomes;
    }
}
