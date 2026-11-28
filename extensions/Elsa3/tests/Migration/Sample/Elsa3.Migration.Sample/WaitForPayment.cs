using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Acme;

// catalog:case-08 begin
// Elsa 3 created a bookmark and resumed on it; Elsa 4 suspends with typed private state and a trigger
// registration, and resumes on the method marked with the registration's resume target.
public sealed class WaitForPayment : StatefulActivity<PaymentResult, PaymentWaitState, PaymentReceived>
{
    public const string StimulusType = "Acme.Payment";
    private const string PaymentTarget = "resume-target:payment";

    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override ValueTask<ActivityTransition<PaymentResult, PaymentWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        var registration = new ActivityTriggerRegistration<PaymentReceived>(PaymentTarget, StimulusType, $"invoice:{InvoiceNumber}");
        return ValueTask.FromResult(Suspend(new PaymentWaitState(InvoiceNumber), [registration]));
    }

    [ResumeTarget(PaymentTarget)]
    protected override ValueTask<ActivityTransition<PaymentResult, PaymentWaitState>> ResumeAsync(
        ActivityResumeContext<PaymentWaitState, PaymentReceived> context) =>
        ValueTask.FromResult(Complete(new PaymentResult(context.State.InvoiceNumber, context.Trigger.Amount)));
}

public sealed record PaymentWaitState(string InvoiceNumber);

public sealed record PaymentReceived(decimal Amount);

public sealed record PaymentResult(
    [property: Output] string InvoiceNumber,
    [property: Output(Key = "Result")] decimal AmountPaid);
// catalog:case-08 end
