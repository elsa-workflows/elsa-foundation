using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-10 begin
// Elsa 3 asked the context for the service (context.GetRequiredService<IInvoiceClient>()).
// Elsa 4 passes it to the constructor; the execution context offers no service lookup.
public sealed class CheckInvoiceStatus(IInvoiceClient invoices) : Activity<InvoiceStatusResult>
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override async ValueTask<ActivityTransition<InvoiceStatusResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ActivityTransition.Complete(new InvoiceStatusResult(await invoices.GetStatusAsync(InvoiceNumber, context.CancellationToken)));
}

public sealed record InvoiceStatusResult([property: Output] string Status);
// catalog:case-10 end
