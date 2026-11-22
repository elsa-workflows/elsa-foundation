using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeHistoryStore
{
    // Per coherent page: bounded batched ancestry reads, never a run-wide subtree download.
    // A missing link or exhausted page budget is explicit incomplete scope, never false exclusion.
    private sealed class Ancestry(EfRuntimeHistoryStore store, string scope, string run, CancellationToken token)
    {
        private readonly Dictionary<string, Mapping.ActivityParent?> parents = new(StringComparer.Ordinal);
        internal async Task Prepare(IEnumerable<(string? Owner, string? Parent)> entries, string requested)
        {
            var frontier = entries.Where(entry => entry.Owner is not null && entry.Owner != requested)
                .Select(entry => entry.Parent ?? entry.Owner!).Where(id => id != requested).Distinct(StringComparer.Ordinal).ToArray();
            for (var depth = 0; depth <= MaximumParentDepth && frontier.Length != 0; depth++)
            {
                token.ThrowIfCancellationRequested();
                var missing = frontier.Where(id => !parents.ContainsKey(id)).Distinct(StringComparer.Ordinal)
                    .Take(Math.Max(0, MaximumAncestryRows - parents.Count)).ToArray();
                if (missing.Length == 0) break;
                var expected = missing.ToDictionary(id => Mapping.Id("activity", scope, run, id), id => id, StringComparer.Ordinal);
                foreach (var batch in expected.Keys.Chunk(100))
                {
                    var rows = await store.Context.Set<RuntimeHistoryActivityEntity>().AsNoTracking().Where(row => batch.Contains(row.Id)).ToArrayAsync(token);
                    foreach (var row in rows)
                    {
                        if (!expected.TryGetValue(row.Id, out var identity)) throw Invalid(run, "an ancestry lookup returned an unexpected identity");
                        parents[identity] = store.mapping.Read(row, scope, run, identity);
                    }
                }
                foreach (var id in missing) parents.TryAdd(id, null);
                frontier = missing.Select(id => parents[id]?.ParentActivityExecutionId).OfType<string>()
                    .Where(id => id != requested && !parents.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
            }
        }
        internal bool? Contains(string? owner, string? requested, bool descendants, string? hint = null)
        {
            if (requested is null) return true;
            if (owner == requested) return true;
            if (!descendants || owner is null) return false;
            var parent = hint;
            if (parent is null)
            { if (!parents.TryGetValue(owner, out var known) || known is null) return null; parent = known.ParentActivityExecutionId; }
            var seen = new HashSet<string>(StringComparer.Ordinal) { owner };
            for (var depth = 0; parent is not null && depth < MaximumParentDepth; depth++)
            {
                if (!seen.Add(parent)) throw Invalid(run, "execution ancestry contains a cycle");
                if (parent == requested) return true;
                if (!parents.TryGetValue(parent, out var known) || known is null) return null;
                parent = known.ParentActivityExecutionId;
            }
            if (parent is not null) throw Invalid(run, "execution ancestry exceeds its bounded depth");
            return false;
        }
    }
    private static bool MatchesFields(RuntimeHistoryEvent item, RuntimeHistoryQuery query) =>
        (query.Category is null || item.Category == query.Category) && (query.Name is null || item.Name == query.Name) &&
        (query.BookmarkId is null || item.BookmarkId == query.BookmarkId);
    private static bool MatchesSearch(RuntimeHistoryEvent item, string? search) => string.IsNullOrEmpty(search) ||
        new[] { item.HistoryEventId, item.Name, item.ActivityExecutionId, item.ExecutableNodeId, item.BookmarkId, item.IncidentId,
            item.CheckpointId, item.PhysicalCommitId, item.Causation?.HistoryEventId, item.Causation?.DeliveryId,
            item.Causation?.CommandId, item.Causation?.SchedulerWorkItemId }.Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    private const string IncompleteScope = "Descendant scope could not be fully established within this bounded window.";
}
