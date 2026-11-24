using System.Buffers.Binary;
using System.Text;
using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed record WorkflowRunQuery(string? Definition = null, ActorStatus? Status = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null);

internal sealed record WorkflowRunView(ActorRef Ref, string? Definition, ActorStatus Status, DateTimeOffset UpdatedAt);

/// <summary>The in-memory workflow run-history path over projected metadata, with newest committed changes first.</summary>
internal sealed class WorkflowRunList(Durable.InMemoryStore store, string workflowActorType, int pageSize)
{
    private const int RefBytes = 64;
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    internal ValueTask<Page<WorkflowRunView>> ListAsync(BoundScope scope, WorkflowRunQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context("workflow-run-history/1", scope, workflowActorType, query);
        Span<byte> bookmark = stackalloc byte[sizeof(long) + RefBytes];
        Durable.InMemoryStore.RunHistoryBookmark? after = null;
        if (from is { } token)
        {
            if (!store.TryOpenReadCursor(context, token.Value, bookmark))
                throw new ArgumentException("The continuation does not match this workflow run list.", nameof(from));
            after = new(BinaryPrimitives.ReadInt64BigEndian(bookmark), Encoding.ASCII.GetString(bookmark[sizeof(long)..]));
        }

        var page = store.ReadFoundationRunHistory(scope.Tenant, workflowActorType, query.Definition, (Durable.InstanceStatus?)query.Status,
            UnixMilliseconds(query.From), UnixMilliseconds(query.To), after, _pageSize + 1);
        var items = page.Rows.Take(_pageSize).Select(row => new WorkflowRunView(new(row.Reference), row.Definition,
            (ActorStatus)row.Status, DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt))).ToArray();
        PageToken? next = null;
        if (page.Rows.Count > _pageSize)
        {
            var last = page.Rows[_pageSize - 1];
            BinaryPrimitives.WriteInt64BigEndian(bookmark, last.UpdatedAt);
            Encoding.ASCII.GetBytes(last.Reference, bookmark[sizeof(long)..]);
            next = new(store.SealReadCursor(context, bookmark));
        }
        return new(new Page<WorkflowRunView>(items, next, page.AsOf));
    }

    internal static byte[] Context(string read, BoundScope scope, string type, WorkflowRunQuery query)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To)
            throw new ArgumentException("The workflow run history start must not be after its end.", nameof(query));
        return Encoding.UTF8.GetBytes(Durable.DurableKeyJson.Write(new
        {
            Read = read, scope.Tenant, Type = type, query.Definition, query.Status, From = query.From?.UtcTicks, To = query.To?.UtcTicks
        }));
    }

    // The foundation clock has millisecond precision. Preserve finer query bounds through comparison.
    internal static decimal? UnixMilliseconds(DateTimeOffset? value) =>
        value is { } instant ? (instant.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (decimal)TimeSpan.TicksPerMillisecond : null;
}
