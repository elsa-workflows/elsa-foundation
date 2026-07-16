using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>
/// A deterministic leaf activity used by execution-test graphs. It records that it ran (by virtue of
/// producing an <see cref="ActivityExecutionState"/>) and emits a fixed set of outcomes, letting tests
/// assert which nodes executed and which connections/branches were followed.
/// </summary>
public sealed class ProbeActivity(IReadOnlyCollection<string> outcomes) : CodeActivity(ProbeActivityType)
{
    /// <summary>The activity type discriminator used for probe nodes.</summary>
    public const string ProbeActivityType = "test/probe";

    protected override void Execute(IActivityExecutionContext context) =>
        context.SetOutcomes(outcomes.ToArray());
}

/// <summary>Descriptor payload carried by a probe node.</summary>
public sealed record ProbeDescriptor(IReadOnlyCollection<string> Outcomes);

/// <summary>Constructs <see cref="ProbeActivity"/> instances from their descriptor.</summary>
public sealed class ProbeActivityConstructor : IActivityConstructor<ProbeDescriptor>
{
    public static string ConsumerKeyValue => typeof(ProbeDescriptor).FullName!;
    public string ConsumerKey => ConsumerKeyValue;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var descriptor = payload.Deserialize<ProbeDescriptor>()
                         ?? throw new InvalidOperationException("Probe descriptor resolved to null.");
        return Construct(descriptor, inputs, outputs, cancellationToken);
    }

    public ValueTask<IActivity> Construct(
        ProbeDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) =>
        new(new ProbeActivity(descriptor.Outcomes));
}
