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
            ? Array.Empty<string>()
            : await SnapshotWaitingExecutionsAsync(request, now, cancellationToken);

        // 2. Start a new instance for every matching published trigger (E3-1).
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

        return lookup.WorkflowExecutionIds;
    }

    private async ValueTask<IReadOnlyList<StimulusStartOutcome>> StartMatchingTriggersAsync(
        StimulusDispatchRequest request,
        IReadOnlyDictionary<string, string> dispatchMetadata,
        CancellationToken cancellationToken)
    {
        var bindings = await _triggerBindingStore.ListByStimulusAsync(request.StimulusType, request.StimulusHash, cancellationToken);

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

            // Spec 089 FR-001: the stimulus payload reaches started instances through the ordinary seed-input
            // channel (the resume path already delivers it via BookmarkResumeDispatchRequest.Input).
            var startInputs = request.Input is { } stimulusInput
                ? new Dictionary<string, object?> { [WellKnownStimulusInputs.StimulusInput] = stimulusInput }
                : null;

            var startRequest = new WorkflowExecutionStartDispatchRequest(
                artifactId: binding.ArtifactId,
                requestedBy: request.RequestedBy,
                idempotencyKey: startIdempotencyKey,
                metadata: dispatchMetadata,
                inputs: startInputs);

            var result = await _startDispatcher.DispatchAsync(startRequest, cancellationToken);
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
                metadata: dispatchMetadata);

            var result = await _resumeDispatcher.DispatchAsync(resumeRequest, cancellationToken);
            outcomes.Add(new StimulusResumeOutcome(workflowExecutionId, result.Status, result.Reason));
        }

        return outcomes;
    }
}
