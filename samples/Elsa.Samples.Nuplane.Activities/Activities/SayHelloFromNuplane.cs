using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Samples.Nuplane.Activities.Activities;

public sealed class SayHelloFromNuplane : ActivityBase
{
    public InputArgument<string>? Recipient { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var name = context.Get(Recipient);
        Console.WriteLine($"Hello {NormalizeName(name)} from a Nuplane-loaded activity.");
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Elsa" : value.Trim();
}
