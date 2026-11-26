using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-06 begin
[ActivityOutcome("Approved")]
[ActivityOutcome("Rejected")]
public sealed class Approve : Activity
{
    [ActivityInput]
    public decimal Amount { get; set; }

    [ActivityInput(DefaultValue = "1000")]
    public decimal Limit { get; set; } = 1000m;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, Amount <= Limit ? "Approved" : "Rejected"));
}
// catalog:case-06 end
