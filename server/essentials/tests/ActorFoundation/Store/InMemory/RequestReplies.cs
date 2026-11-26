using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal static class RequestReplies
{
    internal static async ValueTask<Reply> WaitAsync(Durable.InMemoryStore store, Durable.Requests requests, Durable.Request request,
        BoundScope scope, ActorRef? actor, CancellationToken cancellationToken)
    {
        var completion = requests.CompletionOf(request);
        while (!completion.IsCompleted)
        {
            var remaining = (decimal)request.Deadline - store.FoundationClock();
            if (remaining <= 0)
            {
                requests.Timeout(request);
                break;
            }
            var delay = TimeSpan.FromMilliseconds((double)Math.Min(remaining, 1000));
            try { await completion.WaitAsync(delay, cancellationToken); }
            catch (TimeoutException) { }  // Read the shared clock again, which can step back.
        }

        var reply = await completion.WaitAsync(cancellationToken);
        var status = (ActorStatus?)reply.Status;
        if (reply.Error == "timeout" && actor is { } reference)
            status = (ActorStatus?)store.ReadFoundationActor(scope.Tenant, reference.Id).Actor?.Row.Status;
        return Present(reply, status);
    }

    internal static Reply Present(Durable.Reply reply, ActorStatus? status)
    {
        if (reply.Item is Durable.CreationResult refusal)
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
        return new(reply.Channel, reply.Item, status, reply.Error);
    }
}
