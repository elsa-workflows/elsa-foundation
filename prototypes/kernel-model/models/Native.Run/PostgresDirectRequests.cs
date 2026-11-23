using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

/// <summary>Holds the receiving edge's registrations for the opt-in PostgreSQL direct-request profile.</summary>
internal sealed class PostgresDirectRequests(PostgresRootAdmission admission, EfStore store, string node)
{
    private readonly Requests requests = new();

    internal async ValueTask<bool> ReceiveAsync(string destination, Reply reply, CancellationToken stop)
    {
        if (destination != node) return false;
        return requests.TryReceive(reply, await admission.ReadReplyTimeAsync(stop));
    }

    internal async ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        string id, string kind, string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(scope);
        stop.ThrowIfCancellationRequested();
        if (add?.DueAt is not null) return new(null, null, Error: "requests cannot set a due time");
        if (request.Ambient is not null || request.Inline)
            throw new NotSupportedException("The PostgreSQL reply preview runs its actor on a separate owner.");
        if (request.Deadline <= await admission.ReadReplyTimeAsync(stop)) return new(null, null, Error: "timeout");

        var waiting = requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        try
        {
            var result = await admission.AdmitDirectRequestAsync(scope.Tenant, actor.Id, kind, payload, node, waiting,
                request.ReleaseBeforeCommit, stop, add?.KeepFor);
            if (result == PostgresRootAdmission.DirectResult.Expired) return new(null, null, Error: "timeout");
            if (result is PostgresRootAdmission.DirectResult.Refused or PostgresRootAdmission.DirectResult.MissingTarget)
                return new(null, null, Error: "missing");

            // A duplicate does not attach this new registration to an earlier operation.
            var completion = requests.CompletionOf(waiting);
            while (!completion.IsCompleted)
            {
                var remaining = (decimal)waiting.Deadline - await admission.ReadReplyTimeAsync(stop);
                if (remaining <= 0)
                {
                    requests.Timeout(waiting);
                    break;
                }

                try { await completion.WaitAsync(TimeSpan.FromMilliseconds((double)Math.Min(remaining, 1000)), stop); }
                catch (TimeoutException) { } // Re-read database time before deciding the deadline.
            }

            var reply = await completion.WaitAsync(stop);
            var status = (Contract.ActorStatus?)reply.Status;
            if (reply.Error == "timeout")
                status = (Contract.ActorStatus?)(await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))?.Status;
            return RequestReplies.Present(reply, status);
        }
        finally
        {
            requests.Disconnect(waiting);
        }
    }
}
