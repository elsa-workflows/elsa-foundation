using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-13 begin
// Elsa 3 threw (an incident) or reacted to a cancel signal. Elsa 4 returns a fault or a cancel transition,
// and observes cancellation through the context's token; there is no per-activity cancel callback.
public sealed class ChargeCard : Activity<ChargeResult>
{
    [ActivityInput]
    public decimal Amount { get; set; }

    [ActivityInput]
    public bool CardBlocked { get; set; }

    protected override ValueTask<ActivityTransition<ChargeResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
            return ValueTask.FromResult(ActivityTransition.Cancel<ChargeResult>("The charge was cancelled before it started."));

        if (CardBlocked)
            return ValueTask.FromResult(ActivityTransition.Fault<ChargeResult>(
                new ActivityFault("acme.card-blocked", "The card is blocked.", isRetryable: false)));

        return ValueTask.FromResult(ActivityTransition.Complete(new ChargeResult(Amount)));
    }
}

public sealed record ChargeResult([property: Output(Key = "Result")] decimal Charged);
// catalog:case-13 end
