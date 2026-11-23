using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

/// <summary>Holds the receiving edge's registrations for the opt-in PostgreSQL direct-request profile.</summary>
internal sealed class PostgresDirectRequests(PostgresRootAdmission admission, string node)
{
    private readonly Requests requests = new();

    internal async ValueTask<bool> ReceiveAsync(string destination, Reply reply, CancellationToken stop)
    {
        if (destination != node) return false;
        return requests.TryReceive(reply, await admission.ReadReplyTimeAsync(stop));
    }

    private static Contract.Reply Present(Reply reply) => RequestReplies.Present(reply, (Contract.ActorStatus?)reply.Status);

    internal async ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        string id, string kind, string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(scope);
        stop.ThrowIfCancellationRequested();
        if (add?.DueAt is not null) return new(null, null, Error: "requests cannot set a due time");
        if (request.Ambient is not null || request.Inline)
            throw new NotSupportedException("The PostgreSQL reply preview runs its actor on a separate owner.");
        var untilDeadline = (decimal)request.Deadline - await admission.ReadReplyTimeAsync(stop);
        if (untilDeadline <= 0) return new(null, null, Error: "timeout");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        if (untilDeadline <= uint.MaxValue - 1)
            budget.CancelAfter(TimeSpan.FromMilliseconds((double)untilDeadline));
        var token = budget.Token;
        var waiting = requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        try
        {
            var result = await admission.AdmitDirectRequestAsync(scope.Tenant, actor.Id, kind, payload, node, waiting,
                request.ReleaseBeforeCommit, token, add?.KeepFor);
            if (result == PostgresRootAdmission.DirectResult.Expired) return new(null, null, Error: "timeout");
            if (result is PostgresRootAdmission.DirectResult.Refused or PostgresRootAdmission.DirectResult.MissingTarget)
                return new(null, null, Error: "missing");

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

            return Present(await completion.WaitAsync(token));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            requests.Timeout(waiting);
            return Present(await requests.CompletionOf(waiting));
        }
        finally
        {
            requests.Disconnect(waiting);
        }
    }
}
