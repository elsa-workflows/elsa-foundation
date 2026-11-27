using System.Buffers.Binary;
using System.Text;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Bookmark = Kernel.Durable.FoundationJournalPaging.Bookmark;

namespace Kernel.EntityFramework;

internal class GeneratedRootReads(EfStore store, FoundationReadCursor cursor, int pageSize, string provider)
    : IActorReads, IActorTerminalOutputReads, IActorSequencedJournalReads, IActorPlaybackReads,
      IActorRetentionReads, IActorParkInboxReads
{
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));
    private const int MaxParkInboxCursorBytes = 128 * 1024;

    private sealed record ParkInboxBookmark(FoundationParkMark Mark, long Revision, ParkPageCursor After);

    public bool SupportsParkInbox => store.SupportsParkInbox();

    public async ValueTask<Page<ParkInboxEntry>?> ReadParkInboxAsync(BoundScope scope, ActorRef actor, string selection,
        PageToken? from = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ||
            PostgresRootSelection.OpenPark(cursor, selection, scope.Tenant, actor.Id) is not { Revision: > 0, At: > 0 } mark)
            return null;

        var context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new { Read = $"{provider}-root-park-inbox/1", scope.Tenant, Actor = actor.Id }));
        var after = default(ParkPageCursor);
        var revision = 0L;
        if (from is { } continuation)
        {
            var bookmark = OpenParkInboxBookmark(context, continuation.Value);
            if (bookmark.Mark != mark) return null;
            revision = bookmark.Revision;
            after = bookmark.After;
        }

        var snapshot = await store.ReadRootParkInboxAsync(scope.Tenant, actor.Id, mark, revision, after, _pageSize + 1, cancellationToken);
        if (snapshot is null) return null;
        var rows = snapshot.Rows;
        var page = rows.Take(_pageSize).Select(row => new ParkInboxEntry(
            cursor.InboxHandle(DurableKeyJson.Write(new { scope.Tenant, Actor = actor.Id, mark, row.Sequence, row.Id, row.Scope })),
            row.Kind, row.Reason == ParkKeepReason.Consumed ? DeliveryState.Consumed : DeliveryState.Arrived,
            row.Due, row.Attempts)).ToArray();

        PageToken? next = null;
        if (rows.Count > _pageSize)
        {
            var last = rows[_pageSize - 1];
            var bookmark = new ParkInboxBookmark(mark, snapshot.Revision, new(last.Sequence, new(last.Id, last.Scope)));
            next = new(cursor.Seal(context, Encoding.UTF8.GetBytes(DurableKeyJson.Write(bookmark))));
        }
        return new(page, next, snapshot.AsOf);
    }

    private ParkInboxBookmark OpenParkInboxBookmark(ReadOnlySpan<byte> context, string? token)
    {
        if (string.IsNullOrEmpty(token)) throw InvalidParkInboxCursor();
        var padding = token.EndsWith("==", StringComparison.Ordinal) ? 2 : token.EndsWith('=') ? 1 : 0;
        var length = token.Length / 4 * 3 - padding - FoundationReadCursor.Overhead;
        if (token.Length % 4 != 0 || length is <= 0 or > MaxParkInboxCursorBytes)
            throw InvalidParkInboxCursor();
        Span<byte> plain = length <= 4_096 ? stackalloc byte[length] : new byte[length];
        if (!cursor.TryOpen(context, token, plain)) throw InvalidParkInboxCursor();
        return DurableKeyJson.Read<ParkInboxBookmark>(Encoding.UTF8.GetString(plain));
    }

    private static ArgumentException InvalidParkInboxCursor() => new("The continuation does not match this actor Park inbox.", "from");

    internal ValueTask<ActorRetentionPolicy> ReadRetentionPolicyAsync(BoundScope scope, string actorType, CancellationToken token) =>
        new(store.ReadRootRetentionPolicyAsync(scope, actorType, token));

    internal ValueTask<ActorRetentionPolicyChange> TryApplyRetentionPolicyAsync(BoundScope scope, string actorType, long expectedRevision,
        ActorRetentionWindows proposed, long automaticNewlyEligibleLimit, string? confirmation, CancellationToken token) =>
        new(store.TryApplyRootRetentionPolicyAsync(scope, actorType, expectedRevision, proposed, automaticNewlyEligibleLimit, confirmation, token));

    public ValueTask<ActorRetentionQuote> ReadRetentionQuoteAsync(BoundScope scope, string actorType,
        ActorRetentionWindows previous, ActorRetentionWindows proposed, KeyValuePair<string, string>? definitionLabel = null,
        long? asOf = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new(store.ReadRootRetentionQuoteAsync(scope.Tenant, actorType, previous, proposed, definitionLabel, asOf, cancellationToken));
    }

    public async ValueTask<ActorTerminalOutput?> ReadTerminalOutputAsync(BoundScope scope, ActorRef actor, long endedAt,
        bool includeValues, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PostgresRootAdmission.IsIssuedReferenceShape(actor.Id)) return null;
        return await store.ReadRootTerminalOutputAsync(scope.Tenant, actor.Id, endedAt, includeValues, cancellationToken);
    }

    public async ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PostgresRootAdmission.IsIssuedReferenceShape(actor.Id)) return null;
        var read = await store.ReadRootActorAsync(scope.Tenant, actor.Id, cancellationToken);
        return read is { } found ? View(scope, found.Item, found.AsOf) : null;
    }

    public async ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var labels = query.Labels?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() ?? [];
        var context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new
        {
            Read = $"{provider}-actor-list/1", scope.Tenant, query.Type, query.Status, Labels = labels, query.EndedBefore, query.Parked
        }));
        Span<byte> bookmark = stackalloc byte[64];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor list.", nameof(from));
        var after = from is null ? "" : Encoding.ASCII.GetString(bookmark);
        int? status = query.Status is { } filter ? (int)filter : null;
        var page = await store.ReadRootActorListAsync(scope.Tenant, after, query.Type, status, labels, query.EndedBefore,
            query.Parked, _pageSize + 1, cancellationToken);
        var items = page.Items.Take(_pageSize).Select(row => View(scope, row, page.AsOf)).ToArray();
        PageToken? next = page.Items.Count > _pageSize
            ? new(cursor.Seal(context, Encoding.ASCII.GetBytes(items[^1].Header.Ref.Id))) : null;
        return new(items, next, page.AsOf);
    }

    private ActorView View(BoundScope scope, EfStore.RootListItem row, long asOf)
    {
        var header = new ActorHeader(new(row.Reference), row.Type, scope.Tenant, (ActorStatus)row.Status, row.ParkFault,
            row.ParentReference is { } parent ? new ActorRef(parent) : null, row.EndedAt, row.TypeData, row.Labels);
        var selection = row switch
        {
            { SupportsSelection: true, Status: InstanceStatus.Running } =>
                PostgresRootSelection.SealRunning(cursor, scope.Tenant, row.Reference),
            { TerminalRevision: > 0 and var revision, EndedAt: { } at } =>
                PostgresRootSelection.SealTerminal(cursor, scope.Tenant, row.Reference, revision, at),
            { ParkAt: { } parkedAt, ParkRevision: { } parkRevision } =>
                PostgresRootSelection.SealPark(cursor, scope.Tenant, row.Reference, parkRevision, parkedAt),
            _ => null
        };
        return new(header, selection, row.ParkAt is { } parkAt ? new(parkAt, row.ParkResolution) : null, asOf);
    }

    public bool SupportsSequencedJournal => store.SupportsSequencedJournal();
    public bool SupportsPlayback => store.SupportsPlayback();
    public bool SupportsDescendantPlayback => store.SupportsDescendantPlayback();

    public ValueTask<ActorPlaybackPage?> ReadPlaybackAsync(BoundScope scope, ActorRef actor, ActorPlaybackQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        return new(store.ReadRootPlaybackAsync(scope.Tenant, reference, query, cancellationToken));
    }

    public async ValueTask<ActorSequencedJournalPage?> ReadSequencedJournalAsync(BoundScope scope, ActorRef actor,
        long afterSequence, long? upperSequence, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        return await store.ReadRootSequencedJournalAsync(scope.Tenant, reference, afterSequence, upperSequence, limit, cancellationToken);
    }

    public async ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var context = FoundationJournalPaging.Context(scope.Tenant, actor.Id);
        Span<byte> bookmark = stackalloc byte[Bookmark.Size];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor journal.", nameof(from));
        var after = from is null ? default : Bookmark.Read(bookmark);

        // Issued root references are ASCII hex. An unissued, unrepresentable reference is a
        // normal miss, and an empty read still carries the database clock.
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        var snapshot = await store.ReadRootJournalAsync(scope.Tenant, reference, after.Position, _pageSize + 1, cancellationToken);
        var (entries, last) = FoundationJournalPaging.Read(
            snapshot.Rows.Select(row => (row.Position, row.Revision, row.Entries)), after, _pageSize);

        PageToken? next = entries.Count > 0 ? Seal(last) : null;
        var head = snapshot.Head;
        return new(entries, next, snapshot.AsOf)
        {
            JournalHeadRevision = head?.Revision,
            JournalCatchUpToken = head is null ? null : Seal(Bookmark.AfterPosition(head.Position)),
            JournalCancellation = head is { CancellationRevision: > 0 and var revision, CancellationAt: { } at }
                ? new JournalCancellation(revision, at)
                : null
        };

        PageToken Seal(Bookmark mark)
        {
            Span<byte> landing = stackalloc byte[Bookmark.Size];
            mark.Write(landing);
            return new(cursor.Seal(context, landing));
        }
    }

    public async ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new { Read = $"{provider}-root-inbox/1", scope.Tenant, Actor = actor.Id }));
        // The longest form is operator/ plus the reference, operation digest and occurrence GUID.
        const int occurrenceBytes = 9 + 64 + 1 + 64 + 1 + 32;
        var bookmark = new byte[sizeof(long) + occurrenceBytes];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor inbox.", nameof(from));
        var afterSeq = BinaryPrimitives.ReadInt64LittleEndian(bookmark);
        var afterOccurrence = Encoding.ASCII.GetString(bookmark, sizeof(long), occurrenceBytes).TrimEnd('\0');
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        var snapshot = await store.ReadRootInboxAsync(scope.Tenant, reference, afterSeq, afterOccurrence, _pageSize + 1, cancellationToken);
        var page = snapshot.Rows.Take(_pageSize).Select(row => new DeliveryView(
            cursor.InboxHandle(DurableKeyJson.Write(new { scope.Tenant, Reference = reference, row.Occurrence, row.Seq })),
            row.Id, row.Kind, null, row.Due, row.KeptBy switch
            {
                ParkKeepReason.Consumed => DeliveryState.Consumed,
                ParkKeepReason.Arrived => DeliveryState.Arrived,
                null => DeliveryState.Pending,
                _ => throw new StoreFormatException("The root inbox has an unsupported kept reason.")
            }, row.Attempts, row.Size)).ToArray();
        PageToken? next = null;
        if (snapshot.Rows.Count > _pageSize)
        {
            var last = snapshot.Rows[_pageSize - 1];
            Array.Clear(bookmark);
            BinaryPrimitives.WriteInt64LittleEndian(bookmark, last.Seq);
            Encoding.ASCII.GetBytes(last.Occurrence.AsSpan(), bookmark.AsSpan(sizeof(long)));
            next = new(cursor.Seal(context, bookmark));
        }
        return new(page, next, snapshot.AsOf);
    }
}

internal sealed class PostgresRootReads(EfStore store, FoundationReadCursor cursor, int pageSize)
    : GeneratedRootReads(store, cursor, pageSize, "postgres");

public sealed partial class EfStore
{
    /// <summary>
    /// Opens the public point, list and journal reads for a PostgreSQL generated-root preview.
    /// The ordinary preview uses the public KernelDb context on an already installed database.
    /// The trusted host supplies each BoundScope. The direct-input root profiles also expose their pending inbox metadata.
    /// </summary>
    public async ValueTask<IActorReads> OpenPreviewRootReadsAsync(int pageSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview reads require PostgreSQL.");
        await using var db = create();
        if (!db.Database.IsNpgsql())
            throw new NotSupportedException("Generated-root preview reads require PostgreSQL.");
        var cursor = await PostgresRootAdmission.OpenJournalCursorAsync(db, cancellationToken);
        return new PostgresRootReads(this, cursor, pageSize);
    }
}
