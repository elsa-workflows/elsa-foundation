using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>Elsa 4 test activities that declare Elsa 3 types they replace, scanned into the real catalog by US3 tests.</summary>
public abstract class ReplacingTestActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

/// <summary>A rebuilt custom activity: replaces Elsa 3 <c>Acme.SendInvoice</c>.</summary>
[ReplacesActivityType("Acme.SendInvoice")]
public sealed class SendInvoiceActivity : ReplacingTestActivity
{
    [ActivityInput]
    public string? Customer { get; set; }

    [ActivityInput]
    public decimal Amount { get; set; }
}

/// <summary>One of two activities claiming the same Elsa 3 type.</summary>
[ReplacesActivityType("Acme.Shared")]
public sealed class FirstSharedClaimActivity : ReplacingTestActivity;

/// <summary>The other activity claiming the same Elsa 3 type.</summary>
[ReplacesActivityType("Acme.Shared")]
public sealed class SecondSharedClaimActivity : ReplacingTestActivity;

/// <summary>Claims a built-in Elsa 3 type, which the built-in map keeps.</summary>
[ReplacesActivityType("Elsa.WriteLine")]
public sealed class WriteLineClaimActivity : ReplacingTestActivity;
