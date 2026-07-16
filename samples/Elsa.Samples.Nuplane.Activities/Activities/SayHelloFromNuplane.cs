using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Samples.Nuplane.Activities.Activities;

public sealed class SayHelloFromNuplane(SampleNuplaneActivityOptions options) : Activity<ActivityUnit>
{
    [ActivityInput(Key = "recipient")]
    public string? Recipient { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var message = FormatMessage(NormalizeName(Recipient));
        Console.WriteLine(options.IncludeTimestamp ? $"{DateTimeOffset.Now:HH:mm:ss} {message}" : message);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Elsa" : value.Trim();

    private string FormatMessage(string recipient) =>
        string.IsNullOrWhiteSpace(options.MessageTemplate)
            ? $"Hello {recipient} from a Nuplane-loaded activity."
            : options.MessageTemplate.Replace("{recipient}", recipient, StringComparison.OrdinalIgnoreCase);
}

public sealed record SampleNuplaneActivityOptions(string MessageTemplate, bool IncludeTimestamp);
