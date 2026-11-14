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
    protected override ValueTask<ActivityTransition<CustomerResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new CustomerResult()));
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
