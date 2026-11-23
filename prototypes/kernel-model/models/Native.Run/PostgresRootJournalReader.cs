using Kernel.Foundation.Contracts;
using Kernel.EntityFramework;
using Bookmark = Kernel.Durable.FoundationJournalPaging.Bookmark;

namespace Kernel.Native.Run;

internal sealed class PostgresRootJournalReader(EfStore store, int pageSize)
{
    private readonly FoundationReadCursor _cursor = new();
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    internal async ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var context = FoundationJournalPaging.Context(scope.Tenant, actor.Id);
        Span<byte> bookmark = stackalloc byte[Bookmark.Size];
        if (from is { } token && !_cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor journal.", nameof(from));
        var after = from is null ? default : Bookmark.Read(bookmark);

        // Issued root references are ASCII hex. An unissued, unrepresentable reference is a
        // normal miss, and an empty read still carries the database clock.
        var reference = actor.Id is { Length: 64 } id && id.All(char.IsAsciiHexDigit) ? id : "";
        var snapshot = await store.ReadRootJournalAsync(scope.Tenant, reference, after.Position, _pageSize + 1, cancellationToken);
        var (entries, last) = FoundationJournalPaging.Read(
            snapshot.Rows.Select(row => (row.Position, row.Revision, row.Entries)), after, _pageSize);

        PageToken? next = null;
        if (entries.Count > 0)
        {
            Span<byte> landing = stackalloc byte[Bookmark.Size];
            last.Write(landing);
            next = new(_cursor.Seal(context, landing));
        }
        return new(entries.Select(entry => new JournalEntry(entry.Revision, entry.Entry)).ToArray(), next, snapshot.AsOf);
    }
}
