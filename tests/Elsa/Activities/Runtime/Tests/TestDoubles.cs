using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>A constructor test-double returning a fixed activity. Used to assert registry + factory dispatch.</summary>
internal sealed class FakeConstructorA(string descriptorType) : IActivityConstructor
{
    public IActivity Returned { get; } = new WriteLine();
    public string DescriptorType { get; } = descriptorType;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) => new(Returned);
}

/// <summary>A second, distinct constructor type with the same descriptor type — to trip the dup-guard.</summary>
internal sealed class FakeConstructorB(string descriptorType) : IActivityConstructor
{
    public string DescriptorType { get; } = descriptorType;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) => new((IActivity)new WriteLine());
}
