using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Journal;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;

namespace Elsa.Agent.Activities;

[BackgroundExecutionSafe]
[Activity(IsDiscoverable = false)]
internal sealed class AgentModelTurn(AgentNativeBinding binding, AgentTurnJournal journal, AgentModelRunner runner, TimeProvider time, AgentTurnBudgetAllocator budgets, AgentTurnLeaseRenewer leases) : IActivity, IActivityResult<string>
{
    public async ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context)
    {
        try { return await ExecuteCoreAsync(context); }
        catch (AgentTurnException exception)
        {
            // Authentication/binding and accepted-replay reads precede claim ownership. Preserve
            // their safe domain code without settling a turn this worker has not claimed.
            return ActivityTransition.Fault<string>(new ActivityFault(exception.Code,
                "The retained agent turn is unavailable or does not match this native invocation."));
        }
    }

    private async ValueTask<ActivityTransition> ExecuteCoreAsync(ActivityExecutionContext context)
    {
        var (identity, parent, node) = await binding.ParentAsync(context, context.CancellationToken);
        var handle = AgentNativeBinding.Handle(parent);
        var (document, request) = await journal.ReadAsync(handle, identity, context.CancellationToken);
        AgentNativeBinding.VerifyRequest(request, identity, parent, node);
        async ValueTask VerifyCurrentBindingAsync(CancellationToken cancellationToken)
        {
            var current = await binding.ParentAsync(context, cancellationToken);
            AgentNativeBinding.VerifyRequest(request, current.Identity, current.Parent, current.Node);
        }
        if (document.Status == AgentTurnStatus.Accepted)
        {
            _ = await journal.ReplayAsync(handle, identity, context.InvocationId, context.CancellationToken);
            await VerifyCurrentBindingAsync(context.CancellationToken);
            return ActivityTransition.Complete(handle);
        }
        var dispatched = false;
        var ownsClaim = false;
        var acceptanceStarted = false;
        AgentTurnLeaseLifetime? renewal = null;
        try
        {
            var remaining = document.Deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw AgentModelRunner.Failure("agent.turn.deadline", "The agent deadline expired before dispatch.");
            using var deadline = new CancellationTokenSource(remaining, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, deadline.Token);
            // A redelivered copy of this exact native child may observe the old worker's still-live
            // journal lease. Wait only for this operation, never queue a stale conversation request.
            while (document.Status is AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching && (document.DeliveryProtocol == 1 || document.ChildInvocationId == context.InvocationId) &&
                   document.LeaseExpiresAt is { } expires && expires > time.GetUtcNow())
            {
                var delay = expires - time.GetUtcNow();
                if (delay > TimeSpan.Zero) await Task.Delay(delay < TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1), time, linked.Token);
                document = await journal.ReadStateAsync(document, linked.Token);
            }
            if (document.Status == AgentTurnStatus.Accepted)
            {
                _ = await journal.ReplayAsync(handle, identity, context.InvocationId, linked.Token);
                await VerifyCurrentBindingAsync(linked.Token);
                return ActivityTransition.Complete(handle);
            }
            document = await journal.ClaimAsync(handle, identity, context.InvocationId, time.GetUtcNow(), linked.Token);
            ownsClaim = true;
            await using (renewal = leases.Start(document, linked.Token))
            {
                using var active = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, renewal.LostToken);
                active.Token.ThrowIfCancellationRequested();
                request = await journal.PrepareSessionAsync(document, request, active.Token);
                if (!await journal.DispatchAsync(document, time.GetUtcNow(), active.Token))
                    throw AgentModelRunner.Failure("agent.turn.fence", "The agent turn cannot dispatch under this worker lease.");
                dispatched = true;
                await VerifyCurrentBindingAsync(active.Token);
                var result = await runner.RunAsync(request, active.Token, budgets.Create(document, request, VerifyCurrentBindingAsync));
                renewal.ThrowIfLost();
                await VerifyCurrentBindingAsync(active.Token);
                acceptanceStarted = true;
                if (!await journal.AcceptAsync(document, result, time.GetUtcNow(), active.Token))
                    throw AgentModelRunner.Failure("agent.turn.fence", "The agent result cannot be accepted under this worker lease.");
                await VerifyCurrentBindingAsync(context.CancellationToken);
                return ActivityTransition.Complete(handle);
            }
        }
        catch (Exception exception)
        {
            if (acceptanceStarted && !context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // An acceptance acknowledgement may be lost after its durable commit. Confirm
                    // both current native authority and the exact protected receipt; never infer
                    // success from an attempted write or issue another model request.
                    await VerifyCurrentBindingAsync(context.CancellationToken);
                    _ = await journal.ResultAsync(handle, identity, context.InvocationId, context.CancellationToken);
                    return ActivityTransition.Complete(handle);
                }
                catch { /* Unconfirmed acceptance keeps the original fault/uncertainty below. */ }
            }
            var cause = renewal?.IsLost == true ? renewal.Failure() : exception;
            var code = cause is AgentTurnException agent ? agent.Code : "agent.turn.interrupted";
            var unknown = cause is AgentTurnException { OutcomeUnknown: true } || code == "agent.turn.outcome-unknown" || dispatched && (cause is not AgentTurnException || renewal?.IsLost == true ||
                code is "agent.turn.fence" or "agent.turn.deadline" or "agent.budget.fence" or "agent.budget.deadline");
            // Waiting/admission failure owns no claim. Never settle the operation occupying that fence.
            if (ownsClaim) await journal.FailAsync(document, code, unknown, CancellationToken.None);
            return ActivityTransition.Fault<string>(new ActivityFault(code, "The agent turn failed or was interrupted; inspect its scoped turn status before retrying.",
                category: unknown ? "outcome-unknown" : null));
        }
    }
}
