using System.Threading.Channels;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Durable;

internal enum HeldRootAdmission { Accepted, Duplicate, Refused, Expired }

internal sealed record HeldRootRequest(string Tenant, string Node, Request Caller, bool ReleaseBeforeCommit, TimeSpan? KeepFor)
{
    internal bool IsOpen => Caller.Tenant == Tenant && !Caller.Done;
    internal string Address => Caller.ReplyAddress(Node, ReleaseBeforeCommit);
}

internal interface IHeldRootAdmission
{
    ValueTask<long> ReadClockAsync(CancellationToken token);
    Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token);
    Task<HeldRootAdmission> SendAsync(HeldRootRequest request, string reference, string kind, string? payload, CancellationToken token);
    ValueTask<Contract.ActorStatus?> ReadStatusAsync(string tenant, string reference, CancellationToken token);
}

internal sealed class HeldRootRequests : IReplyTransport
{
    private readonly IHeldRootAdmission admission;
    private readonly string node;
    private readonly string? startType;
    private readonly Requests requests = new();
    // Replies in the order the owner sent them: a turn's response goes out before its end, and the caller takes the first.
    private readonly Channel<Reply> arrivals = Channel.CreateUnbounded<Reply>(new() { SingleReader = true });
    private int delivering;

    internal HeldRootRequests(IHeldRootAdmission admission, string node, string? startType)
    {
        (this.admission, this.node, this.startType) = (admission, node, startType);
        ReplyClock = admission.ReadClockAsync;
    }

    internal Requests Pending => requests;

    internal async ValueTask<bool> ReceiveAsync(string destination, Reply reply, CancellationToken stop)
    {
        if (destination != node) return false;
        return requests.TryReceive(reply, await admission.ReadClockAsync(stop));
    }

    /// <summary>
    /// Delivers an owner's replies to the callers they address, in the order the owner sent them, each checked against
    /// database time on arrival: one reader drains what has arrived, reads the time once, and hands the batch over in order.
    /// A batch whose time cannot be read is kept and retried, ahead of later replies. Not durable, as the transport is not:
    /// replies still waiting when the process ends are lost, and their callers with them.
    /// </summary>
    public void Send(string destination, Reply reply)
    {
        if (destination != node) return;
        arrivals.Writer.TryWrite(reply);
        if (Interlocked.Exchange(ref delivering, 1) == 0) _ = Task.Run(DeliverAsync);
    }

    /// <summary>Database time for a delivered batch; a check substitutes a clock that fails.</summary>
    internal Func<CancellationToken, ValueTask<long>> ReplyClock { get; set; }

    private async Task DeliverAsync()
    {
        var batch = new List<Reply>();
        var backoff = TimeSpan.FromMilliseconds(25);
        while (true)
        {
            while (arrivals.Reader.TryRead(out var next)) batch.Add(next);
            if (batch.Count == 0)
            {
                // A full fence: Send writes and then exchanges the flag, so a reply written after the drain is seen here or
                // by the reader that Send starts.
                Interlocked.Exchange(ref delivering, 0);
                if (!arrivals.Reader.TryPeek(out _) || Interlocked.Exchange(ref delivering, 1) == 1) return;
                continue;
            }
            long receivedAt;
            try { receivedAt = await ReplyClock(CancellationToken.None); }
            catch (Exception)
            {
                // The batch stays, ahead of whatever arrives meanwhile, and is delivered once the time can be read: no
                // later reply (a turn's end) overtakes an earlier one (its response). A caller past its deadline then ends
                // with a timeout on arrival.
                batch.RemoveAll(reply => !requests.HasRegistration(reply.Request));
                if (batch.Count == 0)
                {
                    backoff = TimeSpan.FromMilliseconds(25);
                    continue;
                }
                await Task.Delay(backoff);
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 1_000));
                continue;
            }
            backoff = TimeSpan.FromMilliseconds(25);
            foreach (var reply in batch) requests.TryReceive(reply, receivedAt);
            batch.Clear();
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
            throw new NotSupportedException("The held reply edge runs its actor on a separate owner.");
        StartSpec? spec = null;
        switch (target)
        {
            case Contract.Target.Ref:
                break;
            case Contract.Target.Start start:
                if (startType is null)
                    throw new NotSupportedException("This edge starts no roots: open it with the actor type it starts.");
                // A start this edge does not run: another type, an ephemeral spec, a payload beside the spec.
                if (kind != Contract.Kinds.Start || payload is not null || start.Spec.Type != startType || start.Spec.Ephemeral ||
                    !Enum.IsDefined(start.Spec.History) || !FoundationMetadataLimits.IsOutsideMessageId(id))
                    return new(null, null, Error: "missing");
                spec = new(start.Spec.Type, start.Spec.TypeData, start.Spec.Input, History: (History)start.Spec.History);
                break;
            default:
                throw new NotSupportedException("Held requests must start a root or address an exact root reference.");
        }

        Contract.ActorRef? actor = target is Contract.Target.Ref exact ? exact.Actor : null;
        return await HoldAsync(scope, id, request, add?.KeepFor, IssueAsync, ReadStatusAsync, stop);

        async Task<Contract.Reply?> IssueAsync(HeldRootRequest caller, CancellationToken token)
        {
            if (spec is not null)
            {
                string? started;
                try { started = await admission.StartAsync(caller, spec, token); }
                catch (MetadataLimitExceeded) { return new(null, null, Error: "missing"); }
                if (started is null) return new(null, null, Error: "timeout");
                actor = new Contract.ActorRef(started);
            }
            else
            {
                var result = await admission.SendAsync(caller, actor!.Value.Id, kind, payload, token);
                if (result == HeldRootAdmission.Expired) return new(null, null, Error: "timeout");
                if (result == HeldRootAdmission.Refused) return new(null, null, Error: "missing");
            }
            // A duplicate does not attach this new registration to an earlier operation.
            return null;
        }

        ValueTask<Contract.ActorStatus?> ReadStatusAsync(CancellationToken token) =>
            actor is { } targetActor ? admission.ReadStatusAsync(scope.Tenant, targetActor.Id, token) : ValueTask.FromResult<Contract.ActorStatus?>(null);
    }

    internal async ValueTask<Contract.Reply> RequestOperatorAsync(Contract.BoundScope scope, string id,
        Contract.RequestOptions request, TimeSpan? keepFor, Func<HeldRootRequest, CancellationToken, Task<Contract.Admission?>> issue,
        Func<CancellationToken, ValueTask<Contract.ActorStatus?>> observeStatus, CancellationToken stop)
    {
        var admitted = false;
        return await HoldAsync(scope, id, request, keepFor, IssueAsync, ReadStatusAsync, stop);

        async Task<Contract.Reply?> IssueAsync(HeldRootRequest caller, CancellationToken token)
        {
            var result = await issue(caller, token);
            admitted = result?.Outcome == Contract.AdmissionOutcome.Accepted;
            return result?.Outcome switch
            {
                Contract.AdmissionOutcome.Accepted => null,
                Contract.AdmissionOutcome.Duplicate => new(null, null, Error: "repeat"),
                null => new(null, null, Error: "timeout"),
                _ => new(null, null, Error: result.Reason switch
                {
                    Contract.RefusalReason.StaleSelection => "stale selection",
                    Contract.RefusalReason.TargetMismatch => "target",
                    Contract.RefusalReason.NodeBusy => "operator reply capacity",
                    _ => "missing"
                })
            };
        }

        ValueTask<Contract.ActorStatus?> ReadStatusAsync(CancellationToken token) =>
            admitted ? observeStatus(token) : ValueTask.FromResult<Contract.ActorStatus?>(null);
    }

    private async ValueTask<Contract.Reply> HoldAsync(Contract.BoundScope scope, string id, Contract.RequestOptions request,
        TimeSpan? keepFor, Func<HeldRootRequest, CancellationToken, Task<Contract.Reply?>> issue,
        Func<CancellationToken, ValueTask<Contract.ActorStatus?>> readStatus, CancellationToken stop)
    {
        var untilDeadline = (decimal)request.Deadline - await admission.ReadClockAsync(stop);
        if (untilDeadline <= 0) return await TimeoutAsync();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        if (untilDeadline <= uint.MaxValue - 1)
            budget.CancelAfter(TimeSpan.FromMilliseconds((double)untilDeadline));
        var token = budget.Token;
        var waiting = requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        var caller = new HeldRootRequest(scope.Tenant, node, waiting, request.ReleaseBeforeCommit, keepFor);
        try
        {
            if (await issue(caller, token) is { } immediate)
                return immediate.Error == "timeout" ? await TimeoutAsync() : immediate;

            var completion = requests.CompletionOf(waiting);
            while (!completion.IsCompleted)
            {
                var remaining = (decimal)waiting.Deadline - await admission.ReadClockAsync(token);
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

        async ValueTask<Contract.Reply> TimeoutAsync() => new(null, null, await readStatus(stop), "timeout");

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
                "Parked" => "parked",
                "ParkOutstanding" => "park outstanding",
                "ProtectionUnavailable" => "protection unavailable",
                "OperatorUnavailable" => "operator unavailable",
                _ => "missing"
            });
        return new(reply.Channel, reply.Item, (Contract.ActorStatus?)reply.Status, reply.Error);
    }
}
