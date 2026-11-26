using Kernel.Durable;
using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>
/// The receiving edge of held requests on a PostgreSQL store with held replies (<c>ReplyProtocol</c>): it holds each caller
/// until the owner answers, or until the request's deadline by database time. A request starts a root
/// (<see cref="Contract.Target.Start"/>) or sends to an exact root reference (<see cref="Contract.Target.Ref"/>). It is
/// also the <see cref="IReplyTransport"/> its node's owner answers through when the owner runs in this process
/// (<see cref="PostgresPreviewHosting.StartAsync"/>, its <c>replies</c>).
/// </summary>
/// <remarks>
/// A repeat of an operation reaches what the first one admitted and attaches this caller to nothing: it ends at its own
/// deadline with a timeout and the root's status, as a repeated direct request does.
/// </remarks>
public sealed class PostgresDirectRequests : IReplyTransport
{
    private readonly PostgresRootAdmission admission;
    private readonly HeldRootRequests edge;

    internal PostgresDirectRequests(PostgresRootAdmission admission, EfStore store, string node, string? startType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        if (startType is not null) PostgresRootAdmission.RequireActorType(startType);
        this.admission = admission;
        Node = node;
        edge = new(new Admission(admission, store), node, startType);
    }

    /// <summary>The node whose callers these are: a reply addressed to another node is not this edge's.</summary>
    public string Node { get; }

    /// <summary>How long a start or input operation is deduplicated, for the Foundation opened with these requests.</summary>
    public TimeSpan DeduplicationWindow { get; internal init; } = TimeSpan.FromHours(1);

    /// <summary>The database time, in milliseconds, that a request's deadline is measured against.</summary>
    public ValueTask<long> ReadClockAsync(CancellationToken cancellationToken = default) => admission.ReadReplyTimeAsync(cancellationToken);

    internal Requests Pending => edge.Pending;

    internal ValueTask<bool> ReceiveAsync(string destination, Reply reply, CancellationToken stop) =>
        edge.ReceiveAsync(destination, reply, stop);

    void IReplyTransport.Send(string destination, Reply reply) => edge.Send(destination, reply);

    internal Func<CancellationToken, ValueTask<long>> ReplyClock
    {
        get => edge.ReplyClock;
        set => edge.ReplyClock = value;
    }

    internal ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target,
        string id, string kind, string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken stop) =>
        edge.RequestAsync(scope, target, id, kind, payload, add, request, stop);

    private sealed class Admission(PostgresRootAdmission admission, EfStore store) : IHeldRootAdmission
    {
        public ValueTask<long> ReadClockAsync(CancellationToken token) => admission.ReadReplyTimeAsync(token);

        public async Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token) =>
            (await admission.AdmitRootRequestAsync(request.Tenant, request.Caller.Id, spec, request.Node, request.Caller,
                request.ReleaseBeforeCommit, token, request.KeepFor))?.Reference;

        public async Task<HeldRootAdmission> SendAsync(HeldRootRequest request, string reference, string kind, string? payload,
            CancellationToken token) =>
            (await admission.AdmitDirectRequestAsync(request.Tenant, reference, kind, payload, request.Node, request.Caller,
                request.ReleaseBeforeCommit, token, request.KeepFor)) switch
            {
                PostgresRootAdmission.DirectResult.Accepted => HeldRootAdmission.Accepted,
                PostgresRootAdmission.DirectResult.Duplicate => HeldRootAdmission.Duplicate,
                PostgresRootAdmission.DirectResult.Expired => HeldRootAdmission.Expired,
                _ => HeldRootAdmission.Refused
            };

        public async ValueTask<Contract.ActorStatus?> ReadStatusAsync(string tenant, string reference, CancellationToken token) =>
            (Contract.ActorStatus?)(await store.ReadFirstRootAsync(tenant, reference, token))?.Status;
    }
}
