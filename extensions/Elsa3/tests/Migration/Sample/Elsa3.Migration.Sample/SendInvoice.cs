using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-01 begin
// The Elsa 3 type name Acme.SendInvoice is this class's full name, so no marker is needed (case 2).
// The service comes in through the constructor (case 10).
public sealed class SendInvoice(IInvoiceClient invoices) : Activity<SendInvoiceResult>
{
    // catalog:case-03 begin
    [ActivityInput(DisplayName = "Customer", Description = "The customer to invoice.")]
    [Required]
    public string CustomerId { get; set; } = null!;

    [ActivityInput(Description = "The amount to invoice.")]
    [Required]
    public decimal Amount { get; set; }

    [ActivityInput(DefaultValue = "EUR", UIHint = ActivityInputUIHints.Dropdown, Options = ["EUR", "USD"])]
    public string Currency { get; set; } = "EUR";
    // catalog:case-03 end

    // catalog:case-05 begin
    protected override async ValueTask<ActivityTransition<SendInvoiceResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var invoiceNumber = await invoices.SendAsync(CustomerId, Amount, Currency, context.CancellationToken);
        return ActivityTransition.Complete(new SendInvoiceResult(invoiceNumber, Currency));
    }
    // catalog:case-05 end
}

// catalog:case-04 begin
public sealed record SendInvoiceResult(
    [property: Output(DisplayName = "Invoice number")] string InvoiceNumber,
    [property: Output(Key = "InvoiceCurrency")] string Currency);
// catalog:case-04 end
// catalog:case-01 end
