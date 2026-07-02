using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>
/// A deterministic leaf activity that always faults by throwing during execution. Used by execution-test
/// graphs to assert how composites react to a faulted child (e.g. the fault-aware <c>Parallel</c> join, #308).
/// </summary>
public sealed class FaultingActivity(string message) : CodeActivity(FaultingActivityType)
{
    /// <summary>The activity type discriminator used for faulting nodes.</summary>
    public const string FaultingActivityType = "test/faulting";

    protected override void Execute(IActivityExecutionContext context) =>
        throw new InvalidOperationException(message);
}

/// <summary>Descriptor payload carried by a faulting node.</summary>
public sealed record FaultingDescriptor(string Message);

/// <summary>Constructs <see cref="FaultingActivity"/> instances from their descriptor.</summary>
public sealed class FaultingActivityConstructor : IActivityConstructor<FaultingDescriptor>
{
    public static string DescriptorTypeKey => typeof(FaultingDescriptor).FullName!;
    public string DescriptorType => DescriptorTypeKey;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var descriptor = payload.Deserialize<FaultingDescriptor>()
                         ?? throw new InvalidOperationException("Faulting descriptor resolved to null.");
        return Construct(descriptor, inputs, outputs, cancellationToken);
    }

    public ValueTask<IActivity> Construct(
        FaultingDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) =>
        new(new FaultingActivity(descriptor.Message));
}
