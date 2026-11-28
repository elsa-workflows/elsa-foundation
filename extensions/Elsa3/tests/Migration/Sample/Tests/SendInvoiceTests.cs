using Acme;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa3.Migration.Sample.Tests;

/// <summary>Cases 1–5, 10 and 19: the base class, inputs and defaults, the result record and completion.</summary>
public sealed class SendInvoiceTests
{
    // catalog:case-19 begin
    [Fact]
    public async Task Sends_the_invoice_and_completes_with_its_number()
    {
        var invoices = new FakeInvoiceClient();
        IActivity activity = new SendInvoice(invoices) { CustomerId = "c-1", Amount = 125m };
        var context = new ActivityExecutionContext("workflow-1", "invocation-1", "attempt-1", "node-1", CancellationToken.None);

        var transition = await activity.ExecuteAsync(context);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendInvoiceResult>>(transition);
        Assert.Equal(new SendInvoiceResult("INV-0001", "EUR"), completion.Result);
        Assert.Equal(("c-1", 125m, "EUR"), Assert.Single(invoices.Sent));
    }
    // catalog:case-19 end

    [Fact]
    public void Currency_defaults_to_eur()
    {
        Assert.Equal("EUR", new SendInvoice(new FakeInvoiceClient()).Currency);
    }

    [Fact]
    public async Task Uses_the_given_currency()
    {
        IActivity activity = new SendInvoice(new FakeInvoiceClient()) { CustomerId = "c-2", Amount = 10m, Currency = "USD" };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendInvoiceResult>>(transition);
        Assert.Equal("USD", completion.Result.Currency);
        Assert.Equal(["Done"], completion.Selection.ResolveNames());
    }

    [Fact]
    public async Task Replacing_activity_completes()
    {
        IActivity activity = new Acme.Legacy.Notify { Message = "hello" };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        Assert.IsAssignableFrom<IActivityCompletionTransition<ActivityUnit>>(transition);
    }

    [Fact]
    public async Task Check_invoice_status_reads_through_the_injected_client()
    {
        IActivity activity = new CheckInvoiceStatus(new FakeInvoiceClient()) { InvoiceNumber = "INV-0007" };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<InvoiceStatusResult>>(transition);
        Assert.Equal("Paid:INV-0007", completion.Result.Status);
    }
}
