using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Acme;

// catalog:case-09 begin
// A start trigger: an incoming invoice starts the workflow at this node, and the activity completes with it.
// A custom trigger only starts workflows; it never waits mid-flow (publishing refuses a start trigger that is not
// on the workflow's start path). The stimulus provider below tells publishing which stimulus starts the workflow;
// AcmeFeature registers it.
[TriggerActivity]
public sealed class InvoiceReceived : StatefulTriggerActivity<InvoiceReceivedResult, InvoiceReceivedState, IncomingInvoice>
{
    public const string ActivityType = "Acme.InvoiceReceived";
    public const string StimulusType = "Acme.Invoice";

    [ActivityInput]
    [Required]
    public string Mailbox { get; set; } = null!;

    public static string Hash(string mailbox) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mailbox))).ToLowerInvariant()}";

    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceReceivedState>> ExecuteAsync(
        ActivityStartContext<IncomingInvoice> context) =>
        ValueTask.FromResult(context.TryGetTrigger(out var invoice)
            ? Complete(new InvoiceReceivedResult(invoice.InvoiceNumber))
            : NotStartedByAnInvoice());

    // Never reached: the trigger never suspends.
    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceReceivedState>> ResumeAsync(
        ActivityResumeContext<InvoiceReceivedState, IncomingInvoice> context) =>
        ValueTask.FromResult(NotStartedByAnInvoice());

    private static ActivityTransition<InvoiceReceivedResult, InvoiceReceivedState> NotStartedByAnInvoice() =>
        Fault(new ActivityFault("acme.invoice-trigger-not-started", "InvoiceReceived only starts workflows; it ran without an incoming invoice."));
}

public sealed record IncomingInvoice(string InvoiceNumber);

// The private state a stateful base class requires; a start-only trigger keeps none.
public sealed record InvoiceReceivedState;

public sealed record InvoiceReceivedResult([property: Output(Key = "Result")] string InvoiceNumber);

internal sealed class InvoiceReceivedStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => InvoiceReceived.ActivityType;

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!StringComparer.Ordinal.Equals(node.ActivityType, InvoiceReceived.ActivityType))
            return ActivityTriggerStimulusResult.NotRecognized;

        // The stimulus is fixed at publish time, so the mailbox must be a literal.
        var binding = node.InputBindings.GetValueOrDefault(nameof(InvoiceReceived.Mailbox));
        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { ValueKind: JsonValueKind.String } mailbox)
            throw new ArgumentException($"Trigger node '{node.ExecutableNodeId}' needs a literal mailbox.");

        var stimulus = new TriggerStimulusDescriptor(InvoiceReceived.StimulusType, InvoiceReceived.Hash(mailbox.GetString()!));
        return ActivityTriggerStimulusResult.Recognized([stimulus]);
    }
}
// catalog:case-09 end
