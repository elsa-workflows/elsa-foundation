using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Stages a derived occurrence projection in the native owner's existing transaction. It does not
/// allocate history positions, start/commit transactions, save changes or enable a persistence module.
/// </summary>
internal static partial class EfRuntimeHistoryOccurrenceProjection
{
    private const int LookupBatchSize = 100;

    internal static async ValueTask<int> StageAsync(DbContext context, string scope, string run,
        EfSchemaChain chain, IReadOnlyList<RuntimeHistoryEvent> events, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(chain); ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope); ArgumentException.ThrowIfNullOrWhiteSpace(run);
        RequireTransaction(context); token.ThrowIfCancellationRequested();
        if (events.Count == 0) return 0;
        var types = new[] { typeof(RuntimeHistoryOccurrenceEntity), typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) };
        if (types.Any(type => !chain.ContentColumns.Any(column => column.Entity == type && column.Name == "ContentJson")))
            throw new InvalidOperationException("The owner's schema chain does not cover the occurrence projection content.");
        long previous = 0;
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in events)
        {
            token.ThrowIfCancellationRequested(); RuntimeHistorySafety.ValidateMetadata(item);
            if (item.WorkflowExecutionId != run || item.Sequence <= previous || item.RecordedAt is null ||
                string.IsNullOrWhiteSpace(item.PhysicalCommitId) || !eventIds.Add(item.HistoryEventId) ||
                previous != 0 && item.Sequence != previous + 1)
                throw new ArgumentException("Occurrence staging requires ordered newly committed observations from the exact run.", nameof(events));
            previous = item.Sequence;
        }

        var scopeHash = EfRelationalIdentity.Hash(scope); var runHash = EfRelationalIdentity.Hash(run);
        var identities = events.SelectMany(item => new[] { item.ActivityExecutionId, item.ParentActivityExecutionId })
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var expectedReceipts = identities.ToDictionary(activity => ReceiptId(scope, run, activity), activity => Encode(activity), StringComparer.Ordinal);
        var receiptRows = new Dictionary<string, RuntimeHistoryOccurrenceEntity>(StringComparer.Ordinal);
        foreach (var batch in expectedReceipts.Keys.Chunk(LookupBatchSize))
            foreach (var row in await context.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash && batch.Contains(row.Id)).ToListAsync(token))
                receiptRows.Add(row.Id, row);
        foreach (var row in context.Set<RuntimeHistoryOccurrenceEntity>().Local.Where(row => expectedReceipts.ContainsKey(row.Id))) receiptRows[row.Id] = row;
        var known = new Dictionary<string, Receipt>(StringComparer.Ordinal);
        foreach (var row in receiptRows.Values)
        {
            var data = ReadReceipt(row, scope, run, chain);
            if (data.Activity != expectedReceipts[row.Id]) throw Invalid(run, "receipt hash does not identify the requested complete activity");
            known.Add(data.Activity, data);
        }

        var fresh = new List<Receipt>();
        foreach (var item in events.Where(RuntimeHistoryOccurrence.IsEligible))
        {
            var activity = Encode(item.ActivityExecutionId!);
            var parent = item.ParentActivityExecutionId is null ? null : Encode(item.ParentActivityExecutionId);
            if (known.TryGetValue(activity, out var existing))
            {
                if (item.Sequence < existing.FirstSequence || item.Sequence == existing.FirstSequence &&
                    (existing.FirstEvent != Encode(item.HistoryEventId) || existing.Parent != parent || existing.ObservedAncestryKnown != item.ActivityAncestryKnown))
                    throw Invalid(run, "a replay changes the first occurrence identity or scope evidence");
                continue;
            }
            var ancestors = Array.Empty<string>();
            var ancestryKnown = item.ActivityAncestryKnown == true && parent is null;
            if (item.ActivityAncestryKnown == true && parent is not null && parent != activity &&
                known.TryGetValue(parent, out var prior) && prior.AncestryKnown && prior.Ancestors.Length < 255 &&
                !prior.Ancestors.Contains(activity, StringComparer.Ordinal))
            { ancestors = [parent, .. prior.Ancestors]; ancestryKnown = true; }
            var value = new Receipt(Encode(scope), Encode(run), activity, Encode(item.HistoryEventId), item.Sequence,
                parent, item.ActivityAncestryKnown, ancestryKnown, ancestors);
            known.Add(activity, value); fresh.Add(value);
        }

        var scopes = fresh.SelectMany(value => new string?[] { null, value.Activity }.Concat(value.Ancestors)).Append(null)
            .Distinct(StringComparer.Ordinal).ToArray();
        var expectedCounters = scopes.ToDictionary(value => CounterId(scope, run, value is null ? null : Decode(value)), value => value, StringComparer.Ordinal);
        var counterRows = new Dictionary<string, RuntimeHistoryOccurrenceCounterEntity>(StringComparer.Ordinal);
        foreach (var batch in expectedCounters.Keys.Chunk(LookupBatchSize))
            foreach (var row in await context.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash && batch.Contains(row.Id)).ToListAsync(token))
                counterRows.Add(row.Id, row);
        foreach (var row in context.Set<RuntimeHistoryOccurrenceCounterEntity>().Local.Where(row => expectedCounters.ContainsKey(row.Id))) counterRows[row.Id] = row;
        var originals = counterRows.ToDictionary(pair => pair.Key, pair => ReadCounter(pair.Value, scope, run, chain), StringComparer.Ordinal);
        foreach (var (id, value) in originals)
            if (value.ActivityScope != expectedCounters[id]) throw Invalid(run, "counter hash does not identify the requested complete scope");
        var globalId = CounterId(scope, run, null);
        if (!originals.ContainsKey(globalId) && (receiptRows.Count > 0 ||
            await context.Set<RuntimeHistoryOccurrenceEntity>().AnyAsync(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash, token)))
            throw Invalid(run, "retained occurrence receipts have no allocation counter");
        var missing = expectedCounters.Keys.Except(counterRows.Keys, StringComparer.Ordinal).ToArray();
        foreach (var batch in missing.Chunk(LookupBatchSize))
            if (context.Set<RuntimeHistoryOccurrenceMembershipEntity>().Local.Any(row => batch.Contains(row.ScopeOwnerId)) ||
                await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().AnyAsync(row => batch.Contains(row.ScopeOwnerId), token))
                throw Invalid(run, "retained memberships have no allocation counter");

        var firstPosition = originals.TryGetValue(globalId, out var global) ? global.ProjectionStartsAtSequence : events[0].Sequence;
        global ??= new(Encode(scope), Encode(run), null, 0, firstPosition, firstPosition - 1, null);
        var priorThrough = global.CoveredThroughSequence;
        if (events[0].Sequence > priorThrough + 1 || events[0].Sequence < firstPosition ||
            receiptRows.Values.Any(row => row.FirstSequence < firstPosition || row.FirstSequence > priorThrough))
            throw Invalid(run, "new observations do not extend the acknowledged occurrence prefix");
        var plan = expectedCounters.ToDictionary(pair => pair.Key, pair => originals.GetValueOrDefault(pair.Key) ??
            new Counter(Encode(scope), Encode(run), pair.Value, 0, firstPosition, firstPosition - 1, null), StringComparer.Ordinal);
        var members = new List<RuntimeHistoryOccurrenceMembershipEntity>();
        foreach (var value in fresh)
        {
            if (value.FirstSequence <= priorThrough) throw Invalid(run, "a late projection would change frozen occurrence membership");
            foreach (var declared in new string?[] { null, value.Activity }.Concat(value.Ancestors))
            {
                var id = CounterId(scope, run, declared is null ? null : Decode(declared));
                var counter = plan[id];
                if (value.FirstSequence <= counter.CoveredThroughSequence) throw Invalid(run, "scope occurrence positions are not increasing");
                var member = new Membership(Encode(scope), Encode(run), declared, value.Activity, value.FirstSequence, counter.NextOrdinal);
                members.Add(Row(member, scope, run, chain));
                plan[id] = counter with { NextOrdinal = checked(counter.NextOrdinal + 1), CoveredThroughSequence = value.FirstSequence };
            }
        }
        var currentGlobal = plan[globalId];
        foreach (var item in events.Where(item => item.Category == RuntimeHistoryEventCategory.ActivityLifecycle && !RuntimeHistoryOccurrence.IsEligible(item)))
        {
            if (item.Sequence <= priorThrough && (currentGlobal.FirstUnknownObservationSequence is null || item.Sequence < currentGlobal.FirstUnknownObservationSequence))
                throw Invalid(run, "an unsupported observation cannot be inserted into an old window");
            currentGlobal = currentGlobal with
            { FirstUnknownObservationSequence = Math.Min(currentGlobal.FirstUnknownObservationSequence ?? long.MaxValue, item.Sequence) };
        }
        plan[globalId] = currentGlobal with { CoveredThroughSequence = Math.Max(currentGlobal.CoveredThroughSequence, events[^1].Sequence) };

        // All source/retained/schema validation and overflow checks finish before touching the
        // caller's tracker. A caught validation error cannot publish half an occurrence or counter.
        var counters = plan.Where(pair => !originals.TryGetValue(pair.Key, out var old) || pair.Value != old)
            .Select(pair => Row(pair.Value, scope, run, chain, counterRows.TryGetValue(pair.Key, out var row) ? checked(row.Revision + 1) : 1)).ToArray();
        var receipts = fresh.Select(value => Row(value, scope, run, chain)).ToArray();
        token.ThrowIfCancellationRequested();
        foreach (var row in counters)
        {
            if (!counterRows.TryGetValue(row.Id, out var old)) context.Add(row);
            else
            {
                var entry = context.Entry(old);
                if (entry.State == EntityState.Detached) context.Attach(old);
                entry.CurrentValues.SetValues(row);
            }
        }
        context.AddRange(receipts); context.AddRange(members);
        return receipts.Length;
    }

    /// <summary>Retires identity receipts only with full native run deletion, never prefix content purge.</summary>
    internal static async ValueTask StageDeleteAsync(DbContext context, string scope, string run, CancellationToken token = default)
    {
        RequireTransaction(context); token.ThrowIfCancellationRequested();
        var scopeHash = EfRelationalIdentity.Hash(scope); var runHash = EfRelationalIdentity.Hash(run);
        var receipts = await context.Set<RuntimeHistoryOccurrenceEntity>().Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).ToListAsync(token);
        var members = await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).ToListAsync(token);
        var counters = await context.Set<RuntimeHistoryOccurrenceCounterEntity>().Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).ToListAsync(token);
        receipts = receipts.Concat(context.Set<RuntimeHistoryOccurrenceEntity>().Local).Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).DistinctBy(row => row.Id).ToList();
        members = members.Concat(context.Set<RuntimeHistoryOccurrenceMembershipEntity>().Local).Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).DistinctBy(row => row.Id).ToList();
        counters = counters.Concat(context.Set<RuntimeHistoryOccurrenceCounterEntity>().Local).Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash).DistinctBy(row => row.Id).ToList();
        if (receipts.Any(row => row.ScopeKey != Encode(scope) || row.WorkflowExecutionId != Encode(run)) ||
            members.Any(row => row.ScopeKey != Encode(scope) || row.WorkflowExecutionId != Encode(run)) ||
            counters.Any(row => row.ScopeKey != Encode(scope) || row.WorkflowExecutionId != Encode(run))) throw Invalid(run, "deletion does not identify the exact physical owner");
        context.RemoveRange(members); context.RemoveRange(receipts); context.RemoveRange(counters);
    }

    private static void RequireTransaction(DbContext context)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Occurrence staging requires the native caller's active transaction.");
    }
}
