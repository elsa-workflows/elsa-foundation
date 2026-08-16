using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests.Differential;

/// <summary>
/// The six behavioural dimensions the #646 differential drives. Names are the ledger vocabulary and are
/// recorded verbatim in <c>specs/094-harden-groundwork-stores/divergence-ledger.md</c>.
/// </summary>
internal enum StructuredLogDimension
{
    ConcurrencyConflictShape,
    ProducerOrdering,
    NullAndDefaultMaterialization,
    RollbackVisibility,
    RestartObservation,
    IdempotentReplay
}

/// <summary>
/// A normalized, provider-neutral record of what one comparand did. Values must never carry a cursor
/// payload, connection string, file path, or wall-clock time: replay cursors are opaque by contract and
/// legitimately differ between stacks, so including them would report divergence on every row.
/// </summary>
internal sealed record StructuredLogObservation(StructuredLogDimension Dimension, IReadOnlyDictionary<string, string> Facts)
{
    public string Canonical => string.Join("; ", Facts.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    public override string ToString() => Canonical;
}

/// <summary>
/// Drives one identical operation sequence per dimension against a comparand and reduces it to an
/// observation. Every probe opens, exercises, and leaves the target closed so the caller can run the two
/// stacks in any order without cross-contamination.
/// </summary>
internal static class StructuredLogDifferentialProbes
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-25T00:00:00Z");

    public static Func<StructuredLogDifferentialTarget, Task<StructuredLogObservation>> For(StructuredLogDimension dimension) => dimension switch
    {
        StructuredLogDimension.ConcurrencyConflictShape => ConcurrencyConflictShapeAsync,
        StructuredLogDimension.ProducerOrdering => ProducerOrderingAsync,
        StructuredLogDimension.NullAndDefaultMaterialization => NullAndDefaultMaterializationAsync,
        StructuredLogDimension.RollbackVisibility => RollbackVisibilityAsync,
        StructuredLogDimension.RestartObservation => RestartObservationAsync,
        StructuredLogDimension.IdempotentReplay => IdempotentReplayAsync,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    /// <summary>
    /// The stream is append-only and carries no revision token, so there is no optimistic-concurrency
    /// conflict to detect. The comparable failure shape on this contract is the one the interface
    /// explicitly specifies: invalid, expired, trimmed, wrong-scope and wrong-stream cursors must all
    /// fail the same non-disclosing way. That is what this probe records.
    /// </summary>
    private static async Task<StructuredLogObservation> ConcurrencyConflictShapeAsync(StructuredLogDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        var committed = await store.AppendAsync(Entry(1, "conflict-probe"));
        await store.TrimAsync(0);

        var facts = new Dictionary<string, string>
        {
            ["revision-token"] = "absent-append-only-stream",
            ["trimmed-cursor"] = await FailureShapeAsync(() => store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10)),
            ["foreign-cursor"] = await FailureShapeAsync(() => store.ReadAfterAsync(new StructuredLogReplayCursor("not-a-cursor-from-this-store"), StructuredLogFilter.None, 10)),
            ["default-cursor"] = await FailureShapeAsync(() => store.ReadAfterAsync(default(StructuredLogReplayCursor), StructuredLogFilter.None, 10))
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.ConcurrencyConflictShape, facts);
    }

    /// <summary>
    /// Two producers append concurrently to one stream. The contract promises a gap-free, globally
    /// ordered tail, so the probe records loss, duplication, and whether the drained tail is monotonic.
    /// </summary>
    private static async Task<StructuredLogObservation> ProducerOrderingAsync(StructuredLogDifferentialTarget target)
    {
        const int perProducer = 25;
        var store = await target.OpenAsync();

        await Task.WhenAll(
            Enumerable.Range(0, 2).Select(producer => Task.Run(async () =>
            {
                for (var i = 0; i < perProducer; i++)
                    await store.AppendAsync(Entry(i + 1, $"p{producer}-{i:D3}"));
            })).ToArray());

        var drained = await DrainAsync(store);
        var messages = drained.Select(e => e.Message).ToArray();

        // Sequence is caller-assigned display metadata that concurrent writers may duplicate, so the
        // high-water mark tracks the maximum logical value (perProducer), never the record count.
        var highWater = await store.GetHighWaterMarkAsync();

        var facts = new Dictionary<string, string>
        {
            ["appended"] = (perProducer * 2).ToString(),
            ["readable"] = messages.Length.ToString(),
            ["distinct"] = messages.Distinct(StringComparer.Ordinal).Count().ToString(),
            ["loss"] = messages.Length == perProducer * 2 ? "none" : "present",
            ["per-producer-order"] = PerProducerOrderPreserved(messages) ? "preserved" : "reordered",
            ["high-water-vs-max-logical-sequence"] = highWater == perProducer ? "equal"
                : highWater > perProducer ? "above"
                : "below"
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.ProducerOrdering, facts);
    }

    /// <summary>
    /// Round-trips a fully populated entry and a fully sparse one, recording which fields survive. This
    /// is where a stack that silently drops or defaults a field is caught.
    /// </summary>
    private static async Task<StructuredLogObservation> NullAndDefaultMaterializationAsync(StructuredLogDifferentialTarget target)
    {
        var store = await target.OpenAsync();

        var rich = new StructuredLogEntry
        {
            Sequence = 1,
            Timestamp = FixedNow,
            Level = LogLevel.Error,
            Category = "Rich.Category",
            EventId = 4242,
            EventName = "rich-event",
            Message = "rich message",
            MessageTemplate = "rich {Token}",
            Properties = [new("Token", "value")],
            Scopes = [new([new("k", "v")], "outer")],
            Exception = new("System.InvalidOperationException", "boom", "   at Frame()", null),
            SourceId = "writer"
        };

        var sparse = new StructuredLogEntry
        {
            Sequence = 2,
            Timestamp = FixedNow,
            Level = LogLevel.Trace,
            Category = "Sparse.Category",
            Message = string.Empty,
            SourceId = "writer"
        };

        await store.AppendAsync(rich);
        await store.AppendAsync(sparse);

        var drained = await DrainAsync(store);
        var readRich = drained.SingleOrDefault(e => e.Category == "Rich.Category");
        var readSparse = drained.SingleOrDefault(e => e.Category == "Sparse.Category");

        var facts = new Dictionary<string, string>
        {
            ["rich-readable"] = readRich is null ? "false" : "true",
            ["sparse-readable"] = readSparse is null ? "false" : "true",
            ["event-id"] = RoundTrip(readRich?.EventId?.ToString(), "4242"),
            ["event-name"] = RoundTrip(readRich?.EventName, "rich-event"),
            ["message-template"] = RoundTrip(readRich?.MessageTemplate, "rich {Token}"),
            ["properties"] = RoundTrip(Describe(readRich?.Properties?.Select(p => $"{p.Name}:{p.Value}")), "Token:value"),
            ["scopes"] = RoundTrip(Describe(readRich?.Scopes?.Select(s => s.Text)), "outer"),
            ["exception-type"] = RoundTrip(readRich?.Exception?.Type, "System.InvalidOperationException"),
            ["exception-stack"] = RoundTrip(readRich?.Exception?.StackTrace, "   at Frame()"),
            ["level"] = RoundTrip(readRich?.Level.ToString(), nameof(LogLevel.Error)),
            ["sparse-empty-message"] = readSparse is null ? "unreadable" : readSparse.Message.Length == 0 ? "empty-preserved" : "mutated",
            ["sparse-null-event-id"] = readSparse is null ? "unreadable" : readSparse.EventId is null ? "null-preserved" : "defaulted",
            ["sparse-empty-properties"] = readSparse is null ? "unreadable" : readSparse.Properties.Count == 0 ? "empty-preserved" : "mutated"
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.NullAndDefaultMaterialization, facts);
    }

    /// <summary>
    /// Acknowledged appends must be durable, and an ungraceful stop must not leave a partially applied
    /// batch visible. The probe acknowledges a batch, abandons the process without completing the drain,
    /// then reopens and records what survived.
    /// </summary>
    private static async Task<StructuredLogObservation> RollbackVisibilityAsync(StructuredLogDifferentialTarget target)
    {
        const int acknowledged = 12;
        var store = await target.OpenAsync();

        for (var i = 0; i < acknowledged; i++)
            await store.AppendAsync(Entry(i + 1, $"ack-{i:D3}"));

        var highWaterBefore = await store.GetHighWaterMarkAsync();
        await target.AbandonAsync();

        var reopened = await target.OpenAsync();
        var survived = await DrainAsync(reopened);
        var highWaterAfter = await reopened.GetHighWaterMarkAsync();

        var facts = new Dictionary<string, string>
        {
            ["acknowledged"] = acknowledged.ToString(),
            ["durable-after-abandon"] = survived.Count.ToString(),
            ["acknowledged-writes-durable"] = survived.Count >= acknowledged ? "true" : "false",
            ["torn-batch"] = survived.Count > acknowledged ? "over-committed" : "none",
            ["high-water-preserved"] = highWaterAfter >= highWaterBefore ? "true" : "false"
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.RollbackVisibility, facts);
    }

    /// <summary>
    /// A graceful stop followed by a reopen must preserve the lifetime high-water and the readable tail,
    /// and a cursor issued before the restart must still resolve afterwards.
    /// </summary>
    private static async Task<StructuredLogObservation> RestartObservationAsync(StructuredLogDifferentialTarget target)
    {
        const int before = 8;
        var store = await target.OpenAsync();

        StructuredLogEntry? first = null;
        for (var i = 0; i < before; i++)
        {
            var committed = await store.AppendAsync(Entry(i + 1, $"pre-{i:D3}"));
            first ??= committed;
        }

        var highWaterBefore = await store.GetHighWaterMarkAsync();
        await target.CloseAsync();

        var reopened = await target.OpenAsync();
        var afterRestart = await DrainAsync(reopened);
        var highWaterAfter = await reopened.GetHighWaterMarkAsync();
        var tail = await reopened.GetTailCursorAsync();

        var facts = new Dictionary<string, string>
        {
            ["written-before-restart"] = before.ToString(),
            ["readable-after-restart"] = afterRestart.Count.ToString(),
            ["high-water-preserved"] = highWaterAfter >= highWaterBefore ? "true" : "false",
            ["high-water-rewound"] = highWaterAfter < highWaterBefore ? "true" : "false",
            ["tail-cursor-available"] = tail is null ? "false" : "true",
            ["pre-restart-cursor"] = await FailureShapeAsync(() => reopened.ReadAfterAsync(first!.ReplayCursor, StructuredLogFilter.None, 10))
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.RestartObservation, facts);
    }

    /// <summary>
    /// Records what a caller observes when it re-submits an entry it already appended, and that a
    /// committed cursor stays resolvable when the identical read is replayed.
    /// </summary>
    /// <remarks>
    /// The interface's idempotency clause — "the same accepted append retried after acknowledgement loss
    /// must return the same cursor" — is about a store's <em>internal</em> commit retry, not about a
    /// caller invoking <c>AppendAsync</c> twice. Neither stack exposes a seam to fail a commit between
    /// durable write and acknowledgement without a provider test double, so this probe deliberately does
    /// not claim to cover that clause. Both stacks already have same-stack coverage for it
    /// (<c>GroundworkV2StructuredLogStoreTests</c> and <c>EfCoreStructuredLogStoreResilienceTests</c>);
    /// closing the gap here needs a shared failure-injecting provider, tracked as differential follow-up.
    /// </remarks>
    private static async Task<StructuredLogObservation> IdempotentReplayAsync(StructuredLogDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        var entry = Entry(1, "replayed-once");

        var firstAck = await store.AppendAsync(entry);
        var resubmitAck = await store.AppendAsync(entry);
        var drained = await DrainAsync(store);

        var replayedRead = await store.ReadAfterAsync(firstAck.ReplayCursor, StructuredLogFilter.None, 50);
        var repeatedRead = await store.ReadAfterAsync(firstAck.ReplayCursor, StructuredLogFilter.None, 50);

        var facts = new Dictionary<string, string>
        {
            ["resubmitted-instance-deduplicated"] = CursorsEqual(firstAck.ReplayCursor, resubmitAck.ReplayCursor) ? "true" : "false",
            ["records-for-resubmitted-instance"] = drained.Count(e => e.Message == "replayed-once").ToString(),
            ["repeated-read-is-stable"] = replayedRead.Entries.Count == repeatedRead.Entries.Count ? "true" : "false",
            ["committed-cursor-still-resolves"] = replayedRead.Entries.Count >= 0 ? "true" : "false"
        };

        await target.CloseAsync();
        return new(StructuredLogDimension.IdempotentReplay, facts);
    }

    /// <summary>Reduces an operation to its failure shape, which is what the differential compares.</summary>
    private static async Task<string> FailureShapeAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is StructuredLogReadPage page ? $"accepted:{page.Entries.Count}-entries" : "accepted";
        }
        catch (Exception exception)
        {
            return $"threw:{exception.GetType().Name}";
        }
    }

    /// <summary>
    /// Drains the whole durable tail through the paged replay contract, which is the only read path that
    /// promises gap-free global order.
    /// </summary>
    private static async Task<IReadOnlyList<StructuredLogEntry>> DrainAsync(IStructuredLogStore store)
    {
        var entries = new List<StructuredLogEntry>();
        StructuredLogReplayCursor? cursor = null;

        while (true)
        {
            var page = await store.ReadAfterAsync(cursor, StructuredLogFilter.None, 50);
            entries.AddRange(page.Entries);

            if (!page.HasMore || page.NextCursor is null || CursorsEqual(cursor, page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        return entries;
    }

    private static bool CursorsEqual(StructuredLogReplayCursor? left, StructuredLogReplayCursor? right) =>
        string.Equals(left?.Value, right?.Value, StringComparison.Ordinal);

    private static bool PerProducerOrderPreserved(IEnumerable<string> messages)
    {
        foreach (var group in messages.Where(m => m.StartsWith('p')).GroupBy(m => m[..2], StringComparer.Ordinal))
        {
            var seen = group.ToArray();
            if (!seen.SequenceEqual(seen.OrderBy(m => m, StringComparer.Ordinal)))
                return false;
        }

        return true;
    }

    private static string Describe(IEnumerable<string?>? values) =>
        values is null ? string.Empty : string.Join(",", values);

    private static string RoundTrip(string? actual, string expected) =>
        string.IsNullOrEmpty(actual) ? "dropped" : string.Equals(actual, expected, StringComparison.Ordinal) ? "preserved" : "mutated";

    private static StructuredLogEntry Entry(long sequence, string message) => new()
    {
        Sequence = sequence,
        Timestamp = FixedNow,
        Level = LogLevel.Information,
        Category = "Differential.Category",
        Message = message,
        SourceId = "writer"
    };
}
