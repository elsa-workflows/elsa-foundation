using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-12 begin
// Safe to run in the background: it reads only its inputs, honours the cancellation token, and keys its
// effect on LogicalInvocationId, which stays the same when an operator retries the step (InvocationId does not).
// The workflow author chooses whether a step runs in the background.
[BackgroundExecutionSafe]
public sealed class ArchiveInvoice(IInvoiceClient invoices) : Activity
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        await invoices.ArchiveAsync(InvoiceNumber, context.LogicalInvocationId, context.CancellationToken);
        return ActivityTransition.Complete(ActivityUnit.Value);
    }
}
// catalog:case-12 end
