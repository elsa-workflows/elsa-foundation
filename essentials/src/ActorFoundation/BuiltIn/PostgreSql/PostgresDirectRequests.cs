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
    private readonly EfStore store;
    private readonly string node;
    private readonly string? startType;
    private readonly Requests requests = new();

    internal PostgresDirectRequests(PostgresRootAdmission admission, EfStore store, string node, string? startType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        if (startType is not null) PostgresRootAdmission.RequireActorType(startType);
        (this.admission, this.store, this.node, this.startType) = (admission, store, node, startType);
    }

    /// <summary>The node whose callers these are: a reply addressed to another node is not this edge's.</summary>
    public string Node => node;

    internal async ValueTask<bool> ReceiveAsync(string destination, Reply reply, CancellationToken stop)
    {
        if (destination != node) return false;
        return requests.TryReceive(reply, await admission.ReadReplyTimeAsync(stop));
    }

    /// <summary>
    /// Delivers an owner's reply to the caller it addresses, checked against database time on arrival. Not durable, as the
    /// transport is not: a reply that cannot be checked is dropped, and its caller ends at its deadline.
    /// </summary>
    void IReplyTransport.Send(string destination, Reply reply)
    {
        if (destination != node) return;
        _ = DeliverAsync();

        async Task DeliverAsync()
        {
            try { await ReceiveAsync(destination, reply, CancellationToken.None); }
            catch (Exception) { /* dropped: the caller ends at its deadline */ }
        }
    }

    internal async ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target,
        string id, string kind, string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        stop.ThrowIfCancellationRequested();
        if (add?.DueAt is not null) return new(null, null, Error: "requests cannot set a due time");
        if (request.Ambient is not null || request.Inline)
            throw new NotSupportedException("The PostgreSQL reply preview runs its actor on a separate owner.");
        StartSpec? spec = null;
        switch (target)
        {
            case Contract.Target.Ref:
                break;
            case Contract.Target.Start start:
                if (startType is null)
                    throw new NotSupportedException("This edge starts no roots: open it with the actor type it starts.");
                if (kind != Contract.Kinds.Start || payload is not null || start.Spec.Type != startType || start.Spec.Ephemeral ||
                    !Enum.IsDefined(start.Spec.History))
                    throw new NotSupportedException("The preview starts durable roots of its configured actor type.");
                if (!FoundationMetadataLimits.IsOutsideMessageId(id)) return new(null, null, Error: "missing");
                spec = new(start.Spec.Type, start.Spec.TypeData, start.Spec.Input, History: (History)start.Spec.History);
                break;
            default:
                throw new NotSupportedException("The preview holds requests that start a root or address an exact root reference.");
        }

        Contract.ActorRef? actor = target is Contract.Target.Ref exact ? exact.Actor : null;
        var untilDeadline = (decimal)request.Deadline - await admission.ReadReplyTimeAsync(stop);
        if (untilDeadline <= 0) return await TimeoutAsync();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        if (untilDeadline <= uint.MaxValue - 1)
            budget.CancelAfter(TimeSpan.FromMilliseconds((double)untilDeadline));
        var token = budget.Token;
        var waiting = requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        try
        {
            if (spec is not null)
            {
                PostgresRootAdmission.Receipt? started;
                try
                {
                    started = await admission.AdmitRootRequestAsync(scope.Tenant, id, spec, node, waiting,
                        request.ReleaseBeforeCommit, token, add?.KeepFor);
                }
                catch (MetadataLimitExceeded) { return new(null, null, Error: "missing"); }
                if (started is null) return await TimeoutAsync();
                actor = new Contract.ActorRef(started.Reference);
            }
            else
            {
                var result = await admission.AdmitDirectRequestAsync(scope.Tenant, actor!.Value.Id, kind, payload, node, waiting,
                    request.ReleaseBeforeCommit, token, add?.KeepFor);
                if (result == PostgresRootAdmission.DirectResult.Expired) return await TimeoutAsync();
                if (result is PostgresRootAdmission.DirectResult.Refused or PostgresRootAdmission.DirectResult.MissingTarget)
                    return new(null, null, Error: "missing");
            }

            // A duplicate does not attach this new registration to an earlier operation.
            var completion = requests.CompletionOf(waiting);
            while (!completion.IsCompleted)
            {
                var remaining = (decimal)waiting.Deadline - await admission.ReadReplyTimeAsync(token);
                if (remaining <= 0)
                {
                    requests.Timeout(waiting);
                    break;
                }

                try { await completion.WaitAsync(TimeSpan.FromMilliseconds((double)Math.Min(remaining, 1000)), token); }
                catch (TimeoutException) { } // Re-read database time before deciding the deadline.
            }

            return await PresentAsync(await completion.WaitAsync(token));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            requests.Timeout(waiting);
            return await PresentAsync(await requests.CompletionOf(waiting));
        }
        finally
        {
            requests.Disconnect(waiting);
        }

        // A timeout reports the root's committed status, when there is a root to read.
        async ValueTask<Contract.Reply> TimeoutAsync()
        {
            if (actor is null) return new(null, null, Error: "timeout");
            var committed = await store.ReadFirstRootAsync(scope.Tenant, actor.Value.Id, stop);
            return new(null, null, (Contract.ActorStatus?)committed?.Status, "timeout");
        }

        ValueTask<Contract.Reply> PresentAsync(Reply reply) => reply.Error == "timeout"
            ? TimeoutAsync()
            : ValueTask.FromResult(Present(reply));
    }

    // An owner's answer as the caller sees it; a creation refusal names its reason (as the in-memory foundation's does).
    private static Contract.Reply Present(Reply reply)
    {
        if (reply.Item is CreationResult refusal)
            return new(null, refusal.OwningOperation, Error: refusal.Result switch
            {
                "StaleSelection" => "stale selection",
                "TargetMismatch" => "target",
                "InvalidRetrySelection" => "invalid retry selection",
                "ResolutionOpen" => "resolution open",
                "InProgress" => "in progress",
                "ResolutionUnavailable" => "resolution unavailable",
                "NotEligible" => "not eligible",
                "Protected" => "protected",
                "ProtectionUnavailable" => "protection unavailable",
                "OperatorUnavailable" => "operator unavailable",
                _ => "missing"
            });
        return new(reply.Channel, reply.Item, (Contract.ActorStatus?)reply.Status, reply.Error);
    }
}
