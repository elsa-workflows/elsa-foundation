using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-11 begin
// A variable the author picks: the input is a variable reference, read and written through the step's variables.
public sealed class AddToInvoiceTotal : Activity
{
    [ActivityInput]
    [Required]
    public VariableReference<decimal> Total { get; set; } = null!;

    [ActivityInput]
    public decimal Amount { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Total, context.Variables.Get(Total) + Amount);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}

// A variable read by a name the workflow does not pass in: the activity declares that it does so.
[AccessesUndeclaredVariables]
public sealed class ReadCustomerNote : Activity<CustomerNoteResult>
{
    protected override ValueTask<ActivityTransition<CustomerNoteResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new CustomerNoteResult(context.Variables.Get<string>("customerNote"))));
}

public sealed record CustomerNoteResult([property: Output] string Note);

// Workflow state (here, a workflow output): the activity declares that it accesses it.
[AccessesWorkflowState]
public sealed class PublishInvoiceNumber : Activity
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.WorkflowState.SetOutput("invoiceNumber", JsonSerializer.SerializeToElement(InvoiceNumber));
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
// catalog:case-11 end
