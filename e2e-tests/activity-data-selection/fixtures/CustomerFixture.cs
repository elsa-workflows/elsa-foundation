using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Workbench.ActivityDataSelectionProof;

// TEST ONLY. Explicit Workbench import; absent from standard builds.
// One real Customer object output, never a scalar ID substitute.
[Activity(Icon = "UserRound")]
[ActivityOutcome("Done")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class GetCustomer : Activity<CustomerResult>
{
    [ActivityInput(Key = "customerId", DefaultValue = "cust-1042", DefaultSyntax = "Literal")]
    public string CustomerId { get; set; } = "cust-1042";

    protected override ValueTask<ActivityTransition<CustomerResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new CustomerResult { Customer = new Customer { CustomerId = this.CustomerId } }));
}

public sealed record CustomerResult
{
    [Output(Key = "Customer", Path = "customer")]
    public Customer Customer { get; init; } = new();
}

public sealed record Customer
{
    public string CustomerId { get; init; } = "cust-1042";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "Deterministic customer";

    public string? Nickname { get; init; }
}

// TEST ONLY. A plain String producer that is not a trigger, so a copied whole-value reference can run in any
// position. It returns its authored Value unchanged as its one String Result.
[Activity(Icon = "TextCursorInput")]
[ActivityOutcome("Done")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class EchoValue : Activity<EchoValueResult>
{
    [ActivityInput(Key = "Value")]
    public string Value { get; set; } = "";

    protected override ValueTask<ActivityTransition<EchoValueResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new EchoValueResult(this.Value)));
}

public sealed record EchoValueResult([property: Output(Key = "Result", Path = "result")] string Result);
