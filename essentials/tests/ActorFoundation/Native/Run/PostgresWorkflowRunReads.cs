using System.Buffers.Binary;
using System.Text;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.InMemory;

namespace Kernel.Native.Run;

internal sealed class PostgresWorkflowRunReads(EfStore store, FoundationReadCursor cursor, string actorType, int pageSize)
{
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    internal async ValueTask<Page<WorkflowRunView>> ListAsync(BoundScope scope, WorkflowRunQuery query,
        PageToken? from = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = WorkflowRunList.Context("postgres-workflow-run-history/1", scope, actorType, query);
        Span<byte> bookmark = stackalloc byte[sizeof(long) + 64];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this workflow run list.", nameof(from));
        long? afterTime = from is null ? null : BinaryPrimitives.ReadInt64BigEndian(bookmark);
        var afterRef = from is null ? null : Encoding.ASCII.GetString(bookmark[sizeof(long)..]);
        var lower = WorkflowRunList.UnixMilliseconds(query.From);
        var upper = WorkflowRunList.UnixMilliseconds(query.To);
        // Round inward to representable clock values. Truncating either endpoint changes an inclusive bound.
        var page = await store.ReadRootWorkflowHistoryAsync(scope.Tenant, actorType, query.Definition, (int?)query.Status,
            lower is { } min ? (long)decimal.Ceiling(min) : null, upper is { } max ? (long)decimal.Floor(max) : null,
            afterTime, afterRef, _pageSize + 1, cancellationToken);
        var items = page.Items.Take(_pageSize).Select(row => new WorkflowRunView(new(row.Reference), row.Definition,
            (ActorStatus)row.Status, DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt))).ToArray();
        PageToken? next = null;
        if (page.Items.Count > _pageSize)
        {
            var last = page.Items[_pageSize - 1];
            Span<byte> landing = stackalloc byte[sizeof(long) + 64];
            BinaryPrimitives.WriteInt64BigEndian(landing, last.UpdatedAt);
            Encoding.ASCII.GetBytes(last.Reference, landing[sizeof(long)..]);
            next = new(cursor.Seal(context, landing));
        }
        return new(items, next, page.AsOf);
    }
}
