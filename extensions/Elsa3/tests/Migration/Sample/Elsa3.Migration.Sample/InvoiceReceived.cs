using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Acme;

// catalog:case-09 begin
// A trigger activity: it completes when an incoming invoice starts the workflow at this node, and otherwise
// waits for the next one. The stimulus provider below tells publishing which stimulus starts the workflow;
// AcmeFeature registers it.
[TriggerActivity]
public sealed class InvoiceReceived : StatefulTriggerActivity<InvoiceReceivedResult, InvoiceWaitState, IncomingInvoice>
{
    public const string ActivityType = "Acme.InvoiceReceived";
    public const string StimulusType = "Acme.Invoice";
    private const string InvoiceTarget = "resume-target:invoice";

    [ActivityInput]
    [Required]
    public string Mailbox { get; set; } = null!;

    public static string Hash(string mailbox) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mailbox))).ToLowerInvariant()}";

    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceWaitState>> ExecuteAsync(
        ActivityStartContext<IncomingInvoice> context)
    {
        if (context.TryGetTrigger(out var invoice))
            return ValueTask.FromResult(Complete(new InvoiceReceivedResult(invoice.InvoiceNumber)));

        var registration = new ActivityTriggerRegistration<IncomingInvoice>(InvoiceTarget, StimulusType, Hash(Mailbox));
        return ValueTask.FromResult(Suspend(new InvoiceWaitState(Mailbox), [registration]));
    }

    [ResumeTarget(InvoiceTarget)]
    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceWaitState>> ResumeAsync(
        ActivityResumeContext<InvoiceWaitState, IncomingInvoice> context) =>
        ValueTask.FromResult(Complete(new InvoiceReceivedResult(context.Trigger.InvoiceNumber)));
}

public sealed record IncomingInvoice(string InvoiceNumber);

public sealed record InvoiceWaitState(string Mailbox);

public sealed record InvoiceReceivedResult([property: Output] string InvoiceNumber);

public sealed class InvoiceReceivedStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => InvoiceReceived.ActivityType;

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
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
