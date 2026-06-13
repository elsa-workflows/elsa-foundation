using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Samples.Nuplane.Activities.Activities;

public sealed class SayHelloFromNuplane : ActivityBase
{
    public InputArgument<string>? Recipient { get; set; }

    public string MessageTemplate { get; set; } = "Hello {recipient} from a Nuplane-loaded activity.";

    public bool IncludeTimestamp { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var name = context.Get(Recipient);
        var message = FormatMessage(NormalizeName(name));
        Console.WriteLine(IncludeTimestamp ? $"{DateTimeOffset.Now:HH:mm:ss} {message}" : message);
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Elsa" : value.Trim();

    private string FormatMessage(string recipient) =>
        string.IsNullOrWhiteSpace(MessageTemplate)
            ? $"Hello {recipient} from a Nuplane-loaded activity."
            : MessageTemplate.Replace("{recipient}", recipient, StringComparison.OrdinalIgnoreCase);
}
