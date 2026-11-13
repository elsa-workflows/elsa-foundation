using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointStoreState
{
    // Derived identity/position receipts, published under the existing history transaction/lock.
    // No event content is copied here. Retention can remove content without turning a continuation
    // into a second occurrence; run deletion removes these receipts with the owning history.
    internal sealed record OccurrenceReceipt(string ActivityId, string EventId, long Sequence,
        bool AncestryKnown, IReadOnlyList<string> Ancestors);

    internal Dictionary<(string Scope, string Run), List<OccurrenceReceipt>> HistoryOccurrences { get; } = new();
    internal Dictionary<(string Scope, string Run, string Activity), OccurrenceReceipt> HistoryOccurrenceIds { get; } = new();
    internal Dictionary<(string Scope, string Run, string Activity), List<OccurrenceReceipt>> HistoryOccurrenceScopes { get; } = new();
    internal Dictionary<(string Scope, string Run), List<OccurrenceReceipt>> HistoryOccurrenceUnknownAncestry { get; } = new();
    internal Dictionary<(string Scope, string Run), long> HistoryOccurrenceUnknownObservation { get; } = new();

    private void PublishHistoryOccurrence(string scope, RuntimeHistoryEvent observation)
    {
        var owner = (scope, observation.WorkflowExecutionId);
        if (!RuntimeHistoryOccurrence.IsEligible(observation))
        {
            // Unknown lifecycle semantics cannot silently disappear from a complete-looking
            // occurrence sequence, even after content retention hides that first observation.
            if (observation.Category == RuntimeHistoryEventCategory.ActivityLifecycle)
                HistoryOccurrenceUnknownObservation.TryAdd(owner, observation.Sequence);
            return;
        }
        var key = (scope, observation.WorkflowExecutionId, observation.ActivityExecutionId!);
        if (HistoryOccurrenceIds.ContainsKey(key)) return;
        var ancestors = new List<string>();
        var known = observation.ActivityAncestryKnown == true;
        if (observation.ParentActivityExecutionId is { } parent)
        {
            known = false;
            if (observation.ActivityAncestryKnown == true && parent != key.Item3 &&
                HistoryOccurrenceIds.TryGetValue((scope, owner.Item2, parent), out var prior) &&
                prior.AncestryKnown && prior.Ancestors.Count < 255 && !prior.Ancestors.Contains(key.Item3, StringComparer.Ordinal))
            {
                ancestors.Add(parent); ancestors.AddRange(prior.Ancestors); known = true;
            }
        }
        var receipt = new OccurrenceReceipt(key.Item3, observation.HistoryEventId, observation.Sequence,
            known, ancestors.AsReadOnly());
        HistoryOccurrenceIds.Add(key, receipt);
        if (!HistoryOccurrences.TryGetValue(owner, out var entries)) HistoryOccurrences[owner] = entries = [];
        entries.Add(receipt);
        AddScope(key.Item3);
        foreach (var ancestor in ancestors) AddScope(ancestor);
        if (!known)
        {
            if (!HistoryOccurrenceUnknownAncestry.TryGetValue(owner, out var unknown))
                HistoryOccurrenceUnknownAncestry[owner] = unknown = [];
            unknown.Add(receipt);
        }

        void AddScope(string activity)
        {
            var scopeKey = (scope, owner.Item2, activity);
            if (!HistoryOccurrenceScopes.TryGetValue(scopeKey, out var members)) HistoryOccurrenceScopes[scopeKey] = members = [];
            members.Add(receipt);
        }
    }

    private void DeleteHistoryOccurrences(string scope, string run)
    {
        var owner = (scope, run);
        HistoryOccurrenceUnknownAncestry.Remove(owner);
        HistoryOccurrenceUnknownObservation.Remove(owner);
        if (!HistoryOccurrences.Remove(owner, out var entries)) return;
        foreach (var receipt in entries)
        {
            HistoryOccurrenceIds.Remove((scope, run, receipt.ActivityId));
            HistoryOccurrenceScopes.Remove((scope, run, receipt.ActivityId));
        }
    }
}
