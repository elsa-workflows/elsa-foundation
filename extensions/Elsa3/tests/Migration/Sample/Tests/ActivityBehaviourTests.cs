using System.Text.Json;
using Acme;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa3.Migration.Sample.Tests;

/// <summary>Cases 6, 8, 11, 12, 13 and 14.</summary>
public sealed class ActivityBehaviourTests
{
    [Theory]
    [InlineData(500, "Approved")]
    [InlineData(1000, "Approved")]
    [InlineData(1500, "Rejected")]
    public async Task Approve_selects_the_outcome_by_name(int amount, string outcome)
    {
        IActivity activity = new Approve { Amount = amount };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<ActivityUnit>>(transition);
        Assert.Equal([outcome], completion.Selection.ResolveNames());
    }

    [Fact]
    public async Task Wait_for_payment_suspends_and_resumes_with_the_payment()
    {
        IStatefulActivity<PaymentResult, PaymentWaitState, PaymentReceived> waiting = new WaitForPayment { InvoiceNumber = "INV-0001" };

        var suspended = await waiting.ExecuteAsync(TestContext.Create());

        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<PaymentWaitState>>(suspended);
        var registration = Assert.IsType<ActivityTriggerRegistration<PaymentReceived>>(Assert.Single(suspension.Registrations));
        Assert.Equal(new PaymentWaitState("INV-0001"), suspension.State);
        Assert.Equal(WaitForPayment.StimulusType, registration.StimulusType);

        IStatefulActivity<PaymentResult, PaymentWaitState, PaymentReceived> resumed = new WaitForPayment();
        var completed = await resumed.ResumeAsync(new ActivityResumeContext<PaymentWaitState, PaymentReceived>(
            TestContext.Create(), suspension.State, new PaymentReceived(125m), "registration-1", "delivery-1"));

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<PaymentResult>>(completed);
        Assert.Equal(new PaymentResult("INV-0001", 125m), completion.Result);
    }

    [Fact]
    public async Task Add_to_invoice_total_reads_and_writes_the_referenced_variable()
    {
        var variables = new FakeStepVariables();
        variables.Values["total"] = 100m;
        IActivity activity = new AddToInvoiceTotal { Total = new VariableReference<decimal>("total", "scope-1", "variable-1"), Amount = 25m };

        await activity.ExecuteAsync(TestContext.Create().WithVariables(variables));

        Assert.Equal(125m, variables.Values["total"]);
    }

    [Fact]
    public async Task Read_customer_note_reads_a_variable_by_name()
    {
        var variables = new FakeStepVariables();
        variables.Values["customerNote"] = "Pays late";
        IActivity activity = new ReadCustomerNote();

        var transition = await activity.ExecuteAsync(TestContext.Create().WithVariables(variables));

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<CustomerNoteResult>>(transition);
        Assert.Equal("Pays late", completion.Result.Note);
    }

    [Fact]
    public async Task Publish_invoice_number_sets_a_workflow_output()
    {
        var state = new FakeWorkflowState();
        IActivity activity = new PublishInvoiceNumber { InvoiceNumber = "INV-0001" };

        await activity.ExecuteAsync(TestContext.Create().WithWorkflowState(state));

        Assert.Equal("INV-0001", state.Outputs["invoiceNumber"].GetString());
    }

    [Fact]
    public async Task Archive_invoice_keys_its_effect_on_the_logical_invocation()
    {
        var invoices = new FakeInvoiceClient();
        IActivity activity = new ArchiveInvoice(invoices) { InvoiceNumber = "INV-0001" };
        var retry = TestContext.Create().WithOperationKeys(operationKey: null, targetOperationKey: null, logicalInvocationId: "logical-1");

        await activity.ExecuteAsync(retry);

        Assert.Equal(("INV-0001", "logical-1"), Assert.Single(invoices.Archived));
    }

    [Fact]
    public async Task Archive_invoice_honours_cancellation()
    {
        var invoices = new FakeInvoiceClient();
        IActivity activity = new ArchiveInvoice(invoices) { InvoiceNumber = "INV-0001" };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await activity.ExecuteAsync(TestContext.Create(new CancellationToken(canceled: true))));
        Assert.Empty(invoices.Archived);
    }

    [Fact]
    public async Task Charge_card_faults_when_the_card_is_blocked()
    {
        IActivity activity = new ChargeCard { Amount = 10m, CardBlocked = true };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(transition);
        Assert.Equal("acme.card-blocked", fault.Fault.Code);
        Assert.False(fault.Fault.IsRetryable);
    }

    [Fact]
    public async Task Charge_card_cancels_when_cancellation_was_requested()
    {
        IActivity activity = new ChargeCard { Amount = 10m };

        var transition = await activity.ExecuteAsync(TestContext.Create(new CancellationToken(canceled: true)));

        Assert.IsAssignableFrom<IActivityCancellationTransition>(transition);
    }

    [Fact]
    public async Task Charge_card_completes_with_the_charged_amount()
    {
        IActivity activity = new ChargeCard { Amount = 10m };

        var transition = await activity.ExecuteAsync(TestContext.Create());

        Assert.Equal(new ChargeResult(10m), Assert.IsAssignableFrom<IActivityCompletionTransition<ChargeResult>>(transition).Result);
    }

    [Fact]
    public void Lookup_customer_takes_and_returns_json()
    {
        var function = AcmeScriptFunctions.LookupCustomer;
        var known = function.Implementation(new([JsonSerializer.SerializeToElement("c-1")], null, CancellationToken.None));
        var unknown = function.Implementation(new([JsonSerializer.SerializeToElement("c-9")], null, CancellationToken.None));

        Assert.Equal("acme.lookupCustomer", function.Name);
        Assert.Equal("Acme Retail", known.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, unknown.ValueKind);
    }
}
