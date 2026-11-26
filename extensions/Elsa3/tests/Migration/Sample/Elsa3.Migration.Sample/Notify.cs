using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

// catalog:case-02 begin
namespace Acme.Legacy;

// The Elsa 3 type name Acme.Legacy.Notify-v2 cannot be a namespace and class (it holds a hyphen),
// so the activity names the Elsa 3 type it replaces. Any other Elsa 3 type name is matched by
// naming the namespace and class after it, with no marker (Acme.SendInvoice).
[ReplacesActivityType("Acme.Legacy.Notify-v2")]
public sealed class Notify : Activity
{
    [ActivityInput]
    [Required]
    public string Message { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
// catalog:case-02 end
