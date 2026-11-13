using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using Projection = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceProjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// A private query seam for the owner's selected history reader. This registers no provider or
// capability. The outer adapter captures/rechecks actual access; tokens never confer that access.
internal static class EfRuntimeHistoryOccurrenceReader
{
    private const string Purpose = "runtime-history-occurrences-ef-v1";
    private sealed record Window(string Binding, long Upper, long Purged, string Policy);

    internal static async ValueTask<RuntimeHistoryOccurrencePage> ReadAsync(RuntimeDbContext context, string physicalScope,
        EfSchemaChain callerChain, IRuntimeRecoveryContinuationCodec callerCodec, RuntimeHistoryOccurrenceQuery query,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(callerChain);
        ArgumentNullException.ThrowIfNull(callerCodec); ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalScope); token.ThrowIfCancellationRequested();
        if (System.Transactions.Transaction.Current is not null || context.Database.GetEnlistedTransaction() is not null ||
            context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Occurrence reads require a clean context without ambient or enlisted transactions.");
        var callerTransaction = context.Database.CurrentTransaction;
        if (callerTransaction is not null && callerTransaction.GetDbTransaction().IsolationLevel is not (IsolationLevel.Serializable or IsolationLevel.Snapshot))
            throw new InvalidOperationException("Occurrence reads require a Serializable or Snapshot caller transaction.");
        var binding = RuntimeHistoryIdentity.CreateBinding([physicalScope, query.WorkflowExecutionId,
            query.Limit.ToString(CultureInfo.InvariantCulture), query.AccessBinding ?? "", query.ActivityExecutionId ?? "",
            query.IncludeDescendants.ToString()]);
        var window = Decode(callerCodec, query.SequenceToken, binding);
        await using var ownedTransaction = callerTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        var result = await ReadWindow(context, physicalScope, callerChain, callerCodec, query, binding, window, token);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(token);
        return result;
    }

    private static async Task<RuntimeHistoryOccurrencePage> ReadWindow(RuntimeDbContext context, string scope,
        EfSchemaChain chain, IRuntimeRecoveryContinuationCodec codec, RuntimeHistoryOccurrenceQuery query,
        string binding, Window? window, CancellationToken token)
    {
        var run = query.WorkflowExecutionId; var mapping = new Mapping(chain);
        var headId = Mapping.Id("head", scope, run);
        var headRow = await context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == headId, token);
        var head = headRow is null ? null : mapping.Read(headRow, scope, run);
        var upper = window?.Upper ?? head?.LastSequence ?? 0; var lower = window?.Purged ?? head?.PurgedThroughSequence ?? 0;
        var newer = head is not null && head.LastSequence > upper;
        RuntimeHistoryOccurrencePage Result(RuntimeHistoryOccurrenceAvailability availability, string reason) =>
            new(head, availability, reason, upper, lower, null, null, [], null, newer);
        if (window is not null && (head is null || head.LastSequence < upper || head.PurgedThroughSequence != lower || Policy(head.Policy) != window.Policy))
            return Result(RuntimeHistoryOccurrenceAvailability.Invalidated, "retained-range-changed");
        if (head is null) return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "history-unavailable");
        if (head.HasGaps) return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "history-gap-unlocated");
        if (!head.Policy.SupportedKinds.Any(RuntimeHistoryOccurrence.IsEligibleKind))
            return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "occurrence-recording-unavailable");
        var globalId = Projection.CounterId(scope, run, null);
        var globalRow = await context.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == globalId, token);
        var global = globalRow is null ? null : Checked(run, () => Projection.ReadCounter(globalRow, scope, run, chain));
        if (global is null && head.LastSequence != 0 || global is not null &&
            (global.ProjectionStartsAtSequence != 1 || global.CoveredThroughSequence < head.LastSequence))
            return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "occurrence-recording-unavailable");
        if (global is not null && (global.ActivityScope is not null || global.CoveredThroughSequence > head.LastSequence))
            throw Invalid(run, "the global occurrence counter disagrees with the acknowledged head");
        if (global?.FirstUnknownObservationSequence is { } unknown && unknown <= upper)
            return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "occurrence-observation-unknown");

        Projection.Receipt? exactScope = null;
        if (query.ActivityExecutionId is { } activity)
        {
            exactScope = await Receipt(activity);
            if (exactScope is null || exactScope.FirstSequence > upper)
                return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "scope-unavailable");
            if (query.IncludeDescendants)
            {
                var scopeHash = EfRelationalIdentity.Hash(scope); var runHash = EfRelationalIdentity.Hash(run);
                var unknownRow = await context.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking()
                    .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash && !row.AncestryKnown &&
                        row.FirstSequence > lower && row.FirstSequence <= upper).OrderBy(row => row.FirstSequence).FirstOrDefaultAsync(token);
                if (unknownRow is not null)
                { _ = Checked(run, () => Projection.ReadReceipt(unknownRow, scope, run, chain)); return Result(RuntimeHistoryOccurrenceAvailability.Unavailable, "scope-ancestry-unknown"); }
            }
        }

        long firstOrdinal = 0, count = 0;
        var ownerId = query.IncludeDescendants ? Projection.CounterId(scope, run, query.ActivityExecutionId) : globalId;
        Projection.Counter? source = global;
        if (query.IncludeDescendants)
        {
            var row = await context.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == ownerId, token);
            if (row is null) throw Invalid(run, "the declared scope has no immutable allocation counter");
            source = Checked(run, () => Projection.ReadCounter(row, scope, run, chain));
            if (source.ActivityScope != EfRelationalIdentity.Encode(query.ActivityExecutionId!) || global is null ||
                source.ProjectionStartsAtSequence != global.ProjectionStartsAtSequence || source.CoveredThroughSequence > global.CoveredThroughSequence)
                throw Invalid(run, "the scoped counter disagrees with its declared scope or global prefix");
        }
        var members = context.Set<RuntimeHistoryOccurrenceMembershipEntity>().AsNoTracking().Where(row => row.ScopeOwnerId == ownerId);
        var exactOnly = exactScope is not null && !query.IncludeDescendants;
        if (exactOnly) count = exactScope!.FirstSequence > lower ? 1 : 0;
        else if (source is not null)
        {
            var firstRow = await members.Where(row => row.FirstSequence > lower && row.FirstSequence <= upper)
                .OrderBy(row => row.FirstSequence).FirstOrDefaultAsync(token);
            var lastRow = await members.Where(row => row.FirstSequence <= upper).OrderByDescending(row => row.FirstSequence).FirstOrDefaultAsync(token);
            if (firstRow is not null)
            {
                var first = Member(firstRow); var last = lastRow is null ? throw Invalid(run, "the first member has no frozen final member") : Member(lastRow);
                if (first.Ordinal > last.Ordinal || last.Ordinal >= source.NextOrdinal || last.FirstSequence < first.FirstSequence)
                    throw Invalid(run, "the frozen member boundaries disagree with the allocation counter");
                firstOrdinal = first.Ordinal; count = checked(last.Ordinal - first.Ordinal + 1);
            }
            else if (lastRow is not null) _ = Member(lastRow);
            if (upper == head.LastSequence && lower == 0 && count != source.NextOrdinal)
                throw Invalid(run, "the complete member boundaries disagree with the allocation counter");
        }
        long selected = query.Position ?? 0;
        if (query.AnchorActivityExecutionId is { } anchor)
        {
            var receipt = await Receipt(anchor);
            if (receipt is null || receipt.FirstSequence <= lower || receipt.FirstSequence > upper)
                return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "anchor-outside-range");
            if (exactOnly)
            {
                if (receipt.Activity != exactScope!.Activity) return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "anchor-outside-scope");
                selected = 0;
            }
            else
            {
                var anchorId = Projection.MembershipId(ownerId, anchor);
                var row = await members.SingleOrDefaultAsync(item => item.Id == anchorId, token);
                if (row is null) return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "anchor-outside-scope");
                var member = Member(row);
                if (member.Activity != receipt.Activity || member.FirstSequence != receipt.FirstSequence ||
                    member.Ordinal < firstOrdinal || member.Ordinal - firstOrdinal >= count)
                    throw Invalid(run, "the anchor membership disagrees with its first identity receipt");
                selected = member.Ordinal - firstOrdinal;
            }
        }
        if (selected >= count && (count != 0 || query.Position is not null || query.AnchorActivityExecutionId is not null))
            return Result(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, "position-outside-range");

        var items = new List<RuntimeHistoryOccurrence>();
        if (count > 0)
        {
            var offset = Math.Min(Math.Max(0, count - query.Limit), Math.Max(0, selected - query.Limit / 2));
            var start = checked(firstOrdinal + offset); var amount = (int)Math.Min(query.Limit, count - offset);
            var end = checked(start + amount - 1); var first = firstOrdinal; var last = checked(firstOrdinal + count - 1);
            Projection.Membership[] slice;
            if (exactOnly) slice = [new(EfRelationalIdentity.Encode(scope), EfRelationalIdentity.Encode(run), null,
                exactScope!.Activity, exactScope.FirstSequence, 0)];
            else
            {
                var from = start > first ? start - 1 : start; var through = end < last ? end + 1 : end;
                var rows = await members.Where(row => row.Ordinal >= from && row.Ordinal <= through).OrderBy(row => row.Ordinal)
                    .Take(query.Limit + 2).ToArrayAsync(token);
                slice = rows.Select(Member).ToArray();
                if (slice.Length != through - from + 1 || !slice.Select(row => row.Ordinal).SequenceEqual(Enumerable.Range(0, slice.Length).Select(index => from + index)))
                    throw Invalid(run, "a bounded occurrence range has missing or noncontiguous memberships");
            }
            var receiptIds = slice.Select(row => Projection.ReceiptId(scope, run, EfRelationalIdentity.Decode(row.Activity))).ToArray();
            var receiptRows = await context.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking().Where(row => receiptIds.Contains(row.Id)).ToArrayAsync(token);
            var receipts = receiptRows.ToDictionary(row => row.ActivityExecutionId,
                row => Checked(run, () => Projection.ReadReceipt(row, scope, run, chain)), StringComparer.Ordinal);
            var eventIds = receipts.Values.Select(row => Mapping.Id("event", scope, run, EfRelationalIdentity.Decode(row.FirstEvent))).ToArray();
            var eventRows = await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().Where(row => eventIds.Contains(row.Id)).ToArrayAsync(token);
            var observations = eventRows.ToDictionary(row => row.Id, row => mapping.Read(row, scope, run, head), StringComparer.Ordinal);
            for (var index = 0; index < slice.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var member = slice[index];
                if (!receipts.TryGetValue(member.Activity, out var receipt) || receipt.FirstSequence != member.FirstSequence ||
                    member.FirstSequence <= lower || member.FirstSequence > upper || query.IncludeDescendants &&
                    receipt.Activity != exactScope!.Activity && !receipt.Ancestors.Contains(exactScope.Activity, StringComparer.Ordinal))
                    throw Invalid(run, "an occurrence membership has no matching immutable retained scope receipt");
                var eventId = Mapping.Id("event", scope, run, EfRelationalIdentity.Decode(receipt.FirstEvent));
                if (!observations.TryGetValue(eventId, out var observation) || !RuntimeHistoryOccurrence.IsEligible(observation) ||
                    observation.ActivityExecutionId != EfRelationalIdentity.Decode(receipt.Activity) || observation.Sequence != receipt.FirstSequence ||
                    observation.ParentActivityExecutionId != (receipt.Parent is null ? null : EfRelationalIdentity.Decode(receipt.Parent)) ||
                    observation.ActivityAncestryKnown != receipt.ObservedAncestryKnown)
                    throw Invalid(run, "an occurrence receipt has no matching eligible committed first observation");
                if (!exactOnly && (member.Ordinal < start || member.Ordinal > end)) continue;
                items.Add(new(member.Ordinal - firstOrdinal, observation,
                    member.Ordinal > first && index > 0 ? EfRelationalIdentity.Decode(slice[index - 1].Activity) : null,
                    member.Ordinal < last && index + 1 < slice.Length ? EfRelationalIdentity.Decode(slice[index + 1].Activity) : null));
            }
        }
        var nextToken = query.SequenceToken ?? codec.Encode(Purpose, JsonSerializer.SerializeToUtf8Bytes(new Window(binding, upper, lower, Policy(head.Policy))));
        return new(head, head.CapturedFromStart && lower == 0 ? RuntimeHistoryOccurrenceAvailability.Available : RuntimeHistoryOccurrenceAvailability.Partial,
            lower > 0 ? "retained-prefix-purged" : head.CapturedFromStart ? null : "capture-not-from-start",
            upper, lower, count, count == 0 ? null : selected, items.AsReadOnly(), nextToken, newer);

        Projection.Membership Member(RuntimeHistoryOccurrenceMembershipEntity row)
        {
            var value = Checked(run, () => Projection.ReadMembership(row, scope, run, chain));
            var expected = query.IncludeDescendants ? EfRelationalIdentity.Encode(query.ActivityExecutionId!) : null;
            if (value.ActivityScope != expected) throw Invalid(run, "a membership disagrees with the declared query scope");
            return value;
        }
        async Task<Projection.Receipt?> Receipt(string activity)
        {
            var id = Projection.ReceiptId(scope, run, activity);
            var row = await context.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
            var value = row is null ? null : Checked(run, () => Projection.ReadReceipt(row, scope, run, chain));
            if (value is not null && value.Activity != EfRelationalIdentity.Encode(activity)) throw Invalid(run, "the requested receipt identity disagrees");
            return value;
        }
    }

    private static Window? Decode(IRuntimeRecoveryContinuationCodec codec, string? token, string binding)
    {
        if (token is null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Window>(codec.Decode(Purpose, token));
            if (value is null || value.Binding != binding || value.Upper < 0 || value.Purged < 0 || value.Purged > value.Upper || string.IsNullOrEmpty(value.Policy))
                throw new FormatException();
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException)
        { throw new ArgumentException("The occurrence window is invalid or belongs to another query or authorization context.", nameof(token), error); }
    }
    private static string Policy(RuntimeHistoryCapturePolicy policy) => RuntimeHistoryIdentity.CreateBinding(
        new[] { policy.PolicyId, policy.Version.ToString(CultureInfo.InvariantCulture), policy.Required.ToString() }.Concat(policy.SupportedKinds).ToArray());
    private static T Checked<T>(string run, Func<T> read)
    {
        try { return read(); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException or NotSupportedException or OverflowException)
        { throw new RuntimeHistoryIntegrityException(run, "an occurrence projection cannot be interpreted safely", error); }
    }
    private static RuntimeHistoryIntegrityException Invalid(string run, string reason) => new(run, reason);
}
