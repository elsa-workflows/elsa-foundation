using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed partial class RuntimeHistoryInspectionService
{
    // Optional owners are resolved only after authorization and only when a retained source needs them.
    internal Func<IPostCommitOutboxLookupStore?>? SourceLookup { get; init; }
    internal Func<IDurableTimerHistoryStore?>? TimerLookup { get; init; }

    private static readonly (string Origin, string[] Required)[] SourceGroups =
    [
        ("ChildWorkflowDispatchRequested", ["ChildWorkflowDispatchRequested", "ChildStartAttemptObserved", "ChildStartDispositionObserved"]),
        ("StimulusPublicationCommitted", ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]),
        ("ChildWorkflowSettled", ["ChildWorkflowSettled", "DeliveryReceived", "DeliveryDispatched"])
    ];

    private async ValueTask<(bool? Pending, string? Limitation)> SourceSettlementAsync(string run,
        RuntimeHistoryHead? head, RuntimeHistoryCapturePolicy? policy, CancellationToken token)
    {
        if (head is null || policy is null) return (null, null);
        bool Supports(string kind) => policy.SupportedKinds.Contains(kind, StringComparer.Ordinal);
        var groups = SourceGroups.Where(group => Supports(group.Origin) ||
            group.Origin != "ChildWorkflowSettled" && group.Required.Skip(1).Any(Supports)).ToArray();
        var timers = policy.SupportedKinds.Intersect(DurableTimerHistoryFacts.Kinds, StringComparer.Ordinal).Any();
        var timerUnknown = timers;
        if (groups.Length == 0 && !timers) return (null, null);
        if (!RuntimeHistorySourceFacts.IsFinalProfile(policy) || groups.Any(group => !group.Required.All(Supports)))
            return (null, "The recorded source vocabulary is unsupported for settlement inspection. Recorded events remain readable.");
        if (history is null || !head.CapturedFromStart || head.HasGaps || head.PurgedThroughSequence > 0 ||
            (head.LastSequence == 0 ? head.FirstSequence is not (0 or 1) : head.FirstSequence != 1))
            return (null, "Unavailable history prevents establishing all source responsibility for this recorded prefix.");

        try
        {
            const int sourceLimitPerKind = 100;
            var binding = await BindingAsync(token);
            var checkedSources = new Dictionary<string, string>(StringComparer.Ordinal);
            IPostCommitOutboxLookupStore? lookup = null;
            var pending = false;
            // An incomplete timer vocabulary stays unknown, while another known owed source still proves pending.
            var timerOwner = timers && DurableTimerHistoryFacts.Kinds.All(Supports) ? TimerLookup?.Invoke() : null;
            if (timerOwner is { SupportsHistoryResponsibility: true } && timerOwner.CanInspectHistorySources(history))
            {
                pending = await timerOwner.HasPendingHistoryAsync(run, token);
                timerUnknown = false;
            }
            bool TimerAuthorityChanged() => !timerUnknown && timerOwner is not null &&
                (!timerOwner.SupportsHistoryResponsibility || !timerOwner.CanInspectHistorySources(history));
            foreach (var group in groups)
            {
                // Bounded indexed reads of origin kinds. Never walk all events or enumerate the global outbox.
                var page = await history.ReadPageAsync(new(run, sourceLimitPerKind, name: group.Origin, accessBinding: binding), token);
                if (!SameSourceBoundary(head, page.Head) || page.UpperSequence != head.LastSequence ||
                    page.ScannedThroughSequence != page.UpperSequence || page.NextContinuationToken is not null ||
                    !page.ScopeComplete || page.Items.Count > sourceLimitPerKind || page.ScannedCount < page.Items.Count)
                    return (null, "The bounded source inspection could not establish this recorded range. More or newer source history may remain.");
                long previous = 0;
                foreach (var item in page.Items)
                {
                    EnsureEventOwner(run, item); EnsureEventPosition(item, head);
                    if (item.Sequence <= previous || item.Authority != RuntimeHistoryAuthority.CommittedTransition || item.Name != group.Origin)
                        throw new RuntimeHistoryIntegrityException(run, "source inspection disagrees with its committed origin range");
                    previous = item.Sequence;
                    if (item.SchemaVersion != 1)
                        return (null, "A recorded source version is unsupported for settlement inspection.");
                    // Causation identifies the incoming trigger, which can be a different delivery.
                    // Only an explicit outgoing source identity can establish this event's responsibility.
                    if (!item.Metadata.TryGetValue("history.sourceDeliveryId", out var sourceId) || string.IsNullOrWhiteSpace(sourceId))
                        return (null, "A recorded source has no inspectable responsibility identity.");
                    if (checkedSources.TryGetValue(sourceId, out var original))
                    {
                        if (original != item.HistoryEventId)
                            throw new RuntimeHistoryIntegrityException(run, "one source has conflicting recorded origins");
                        continue;
                    }
                    checkedSources.Add(sourceId, item.HistoryEventId);
                    lookup ??= SourceLookup?.Invoke();
                    if (lookup is null || !lookup.CanInspectHistorySources(history))
                        return (null, "The selected source and history providers cannot establish one scoped responsibility owner.");
                    var source = await lookup.FindDurableAsync(sourceId, token);
                    if (source is null)
                        return (null, "Part of the recorded source responsibility is no longer inspectable.");
                    // A child's terminal origin can address its parent, rather than the run being inspected.
                    var target = group.Origin == "ChildWorkflowSettled"
                        ? item.Metadata.GetValueOrDefault("history.relatedWorkflowExecutionId") : run;
                    var intentKind = group.Origin switch
                    {
                        "ChildWorkflowDispatchRequested" => WorkflowDispatchLifecycle.StartChildIntentKind,
                        "ChildWorkflowSettled" => WorkflowDispatchLifecycle.ResumeParentIntentKind,
                        _ => "Elsa.Activities.Primitives.PublishStimulus"
                    };
                    if (source.SourceKind != RuntimeDeliverySourceKind.WorkflowCheckpoint || source.Intent is null ||
                        string.IsNullOrWhiteSpace(target) || source.OutboxItemId != sourceId ||
                        source.Intent.WorkflowExecutionId != target || source.Intent.Kind != intentKind ||
                        source.Intent.HistoryCausation is not { } cause || cause.WorkflowExecutionId != run ||
                        cause.HistoryEventId != item.HistoryEventId || cause.DeliveryId != sourceId)
                        throw new RuntimeHistoryIntegrityException(run, "durable source disagrees with its recorded origin");
                    if (!Enum.IsDefined(source.Status))
                        throw new RuntimeHistoryIntegrityException(run, "durable source has an unknown delivery status");
                    // Failed-final/cancelled/delayed work does not prove its history responsibility acknowledged.
                    pending |= source.Status != RuntimePostCommitOutboxStatus.Delivered;
                }
            }
            if (await BindingAsync(token) != binding || lookup is not null && !lookup.CanInspectHistorySources(history) || TimerAuthorityChanged())
                return (null, "The inspection context changed while source responsibility was read. Refresh to inspect its current boundary.");
            if (pending) return (true, timerUnknown ? "Timer history settlement remains unknown." : null);
            // Even absence is only an observation of this exact retained prefix, never future source closure.
            // Keep the original page's head/bounds; a concurrent append, purge or policy change invalidates the negative.
            if (!SameSourceBoundary(head, await history.FindHeadAsync(run, token)))
                return (null, "History changed while source settlement was read. Refresh to inspect its current boundary.");
            if (await BindingAsync(token) != binding || lookup is not null && !lookup.CanInspectHistorySources(history) || TimerAuthorityChanged())
                return (null, "The inspection context changed while source responsibility was read. Refresh to inspect its current boundary.");
            return timerUnknown ? (null, "Timer history settlement remains unknown.") : (false, null);
        }
        catch (Exception error) when (IsMalformedSource(error))
        {
            token.ThrowIfCancellationRequested();
            throw new RuntimeHistoryIntegrityException(run, "source inspection could not interpret its retained owner evidence", error);
        }
        catch (Exception error) when (IsAvailabilityFailure(error) || error is OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            logger?.LogWarning("Source history responsibility could not be read ({FailureType}).", error.GetType().Name);
            return (null, "Source history settlement could not be checked. Recorded events remain readable.");
        }
    }

    private static bool IsMalformedSource(Exception error) => error is InvalidDataException or System.Text.Json.JsonException or RuntimeHistoryIntegrityException ||
        error.InnerException is { } inner && IsMalformedSource(inner);

    private static bool SameSourceBoundary(RuntimeHistoryHead expected, RuntimeHistoryHead? actual) => actual is not null &&
        expected.WorkflowExecutionId == actual.WorkflowExecutionId && expected.FirstSequence == actual.FirstSequence &&
        expected.LastSequence == actual.LastSequence && expected.LastPhysicalCommitId == actual.LastPhysicalCommitId &&
        expected.LastCommittedAt.EqualsExact(actual.LastCommittedAt) && expected.Policy.IsEquivalentTo(actual.Policy) &&
        expected.CapturedFromStart == actual.CapturedFromStart && expected.HasGaps == actual.HasGaps &&
        expected.PurgedThroughSequence == actual.PurgedThroughSequence;
}
