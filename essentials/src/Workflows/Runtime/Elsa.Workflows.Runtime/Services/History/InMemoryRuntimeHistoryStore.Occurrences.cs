using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using OccurrenceReceipt = Elsa.Workflows.Runtime.Services.Checkpoints.InMemoryRuntimeCheckpointStoreState.OccurrenceReceipt;

namespace Elsa.Workflows.Runtime.Services.History;

internal sealed partial class InMemoryRuntimeHistoryStore
{
    private const string OccurrencePurpose = "runtime-history-occurrences-v1";

    public ValueTask<RuntimeHistoryOccurrencePage> ReadOccurrencesAsync(RuntimeHistoryOccurrenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        var binding = Binding(scope, query.WorkflowExecutionId, query.Limit, query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants);
        var cursor = DecodeOccurrence(query.SequenceToken, binding);
        lock (state.SyncRoot)
        {
            var owner = (scope, query.WorkflowExecutionId);
            var head = state.HistoryHeads.GetValueOrDefault(owner);
            ValidateHead(query.WorkflowExecutionId, head);
            var upper = cursor?.Upper ?? head?.LastSequence ?? 0;
            var lower = cursor?.Purged ?? head?.PurgedThroughSequence ?? 0;
            var newer = head is not null && head.LastSequence > upper;
            if (cursor is not null && (head is null || head.LastSequence < upper || head.PurgedThroughSequence != lower ||
                PolicyBinding(head.Policy) != cursor.Policy))
                return Result(RuntimeHistoryOccurrenceAvailability.Invalidated, "retained-range-changed");
            if (head is null) return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "history-unavailable");
            if (head.HasGaps) return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "history-gap-unlocated");
            if (state.HistoryOccurrenceUnknownObservation.TryGetValue(owner, out var unknownObservation) && unknownObservation <= upper)
                return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "occurrence-observation-unknown");
            if (!head.Policy.SupportedKinds.Any(RuntimeHistoryOccurrence.IsEligibleKind))
                return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "occurrence-recording-unavailable");

            IReadOnlyList<OccurrenceReceipt> source = state.HistoryOccurrences.GetValueOrDefault(owner) ?? [];
            if (query.ActivityExecutionId is { } activity)
            {
                if (!state.HistoryOccurrenceIds.TryGetValue((scope, owner.Item2, activity), out var exactScope) ||
                    exactScope.Sequence > upper)
                    return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "scope-unavailable");
                if (query.IncludeDescendants)
                {
                    // First-observation membership is immutable. Later ancestry enrichment never
                    // repairs an already admitted range into a different count or ordinal order.
                    var unknown = state.HistoryOccurrenceUnknownAncestry.GetValueOrDefault(owner);
                    if (unknown is not null && FindOccurrenceAfter(unknown, upper) > FindOccurrenceAfter(unknown, lower))
                        return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "scope-ancestry-unknown");
                    source = state.HistoryOccurrenceScopes.GetValueOrDefault((scope, owner.Item2, activity)) ?? [];
                }
                else source = [exactScope];
            }

            var first = FindOccurrenceAfter(source, lower);
            var end = FindOccurrenceAfter(source, upper);
            var count = end - first;
            long selected = query.Position ?? 0;
            if (query.AnchorActivityExecutionId is { } anchor)
            {
                if (!state.HistoryOccurrenceIds.TryGetValue((scope, owner.Item2, anchor), out var receipt) ||
                    receipt.Sequence <= lower || receipt.Sequence > upper)
                    return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "anchor-outside-range");
                var index = FindOccurrenceAfter(source, receipt.Sequence) - 1;
                if (index < first || index >= end || source[index].ActivityId != anchor)
                    return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "anchor-outside-scope");
                selected = index - first;
            }
            if (selected >= count && (count != 0 || query.Position is not null || query.AnchorActivityExecutionId is not null))
                return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "position-outside-range");

            var offset = (int)Math.Min(Math.Max(0, count - query.Limit), Math.Max(0, selected - query.Limit / 2));
            var items = new List<RuntimeHistoryOccurrence>(Math.Min(count, query.Limit));
            for (var index = first + offset; index < end && items.Count < query.Limit; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var receipt = source[index];
                var observation = state.HistoryEventsById.GetValueOrDefault((scope, owner.Item2, receipt.EventId));
                if (observation is null || observation.WorkflowExecutionId != owner.Item2 ||
                    observation.ActivityExecutionId != receipt.ActivityId || observation.Sequence != receipt.Sequence ||
                    observation.Sequence <= lower || observation.Sequence > upper || observation.RecordedAt is null ||
                    observation.PhysicalCommitId is null || !RuntimeHistoryOccurrence.IsEligible(observation))
                    throw new RuntimeHistoryIntegrityException(owner.Item2, "occurrence receipt has no matching committed observation");
                items.Add(new(index - first, observation,
                    index > first ? source[index - 1].ActivityId : null,
                    index + 1 < end ? source[index + 1].ActivityId : null));
            }
            var token = query.SequenceToken ?? codec.Encode(OccurrencePurpose, JsonSerializer.SerializeToUtf8Bytes(
                new OccurrenceCursor(binding, upper, lower, PolicyBinding(head.Policy))));
            return ValueTask.FromResult(new RuntimeHistoryOccurrencePage(head,
                head.CapturedFromStart && lower == 0 ? RuntimeHistoryOccurrenceAvailability.Available : RuntimeHistoryOccurrenceAvailability.Partial,
                lower > 0 ? "retained-prefix-purged" : head.CapturedFromStart ? null : "capture-not-from-start",
                upper, lower, count, count == 0 ? null : selected, items.AsReadOnly(), token, newer));

            ValueTask<RuntimeHistoryOccurrencePage> Result(RuntimeHistoryOccurrenceAvailability availability, string reason) =>
                ValueTask.FromResult(new RuntimeHistoryOccurrencePage(head, availability, reason, upper, lower, null, null, [], null, newer));
        }
    }

    // Binary lookup in provider-owned occurrence indexes; neither lifecycle volume nor an earlier
    // page traversal affects the work required to seek a distant ordinal or exact occurrence.
    internal static int FindOccurrenceAfter(IReadOnlyList<OccurrenceReceipt> source, long sequence)
    {
        var low = 0; var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].Sequence <= sequence) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static string PolicyBinding(RuntimeHistoryCapturePolicy policy) => RuntimeHistoryIdentity.CreateBinding(
        new[] { policy.PolicyId, policy.Version.ToString(CultureInfo.InvariantCulture), policy.Required.ToString() }
            .Concat(policy.SupportedKinds).ToArray());

    private OccurrenceCursor? DecodeOccurrence(string? token, string binding)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<OccurrenceCursor>(codec.Decode(OccurrencePurpose, token));
            if (cursor is null || cursor.Binding != binding || cursor.Upper < 0 || cursor.Purged < 0 ||
                cursor.Purged > cursor.Upper || string.IsNullOrEmpty(cursor.Policy)) throw new FormatException();
            return cursor;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("The occurrence window is invalid or belongs to another query or authorization context.", nameof(token), error);
        }
    }

    private sealed record OccurrenceCursor(string Binding, long Upper, long Purged, string Policy);
}
