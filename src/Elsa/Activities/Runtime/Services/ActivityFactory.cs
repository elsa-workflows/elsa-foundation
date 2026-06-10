using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Pure dispatch / lifecycle orchestrator. Ensures contributed constructors are visible to the
/// registry, resolves the constructor registered for the descriptor type, and delegates construction
/// to it. No type resolution or argument binding lives here — those are kind-specific and owned by
/// each <see cref="IActivityConstructor"/>.
/// </summary>
public sealed class ActivityFactory : IActivityFactory
{
    private readonly IActivityConstructorRegistry _registry;

    public ActivityFactory(
        IActivityConstructorRegistry registry,
        IEnumerable<IActivityConstructor>? constructors = null)
    {
        registry.AddAll(constructors ?? []);
        _registry = registry;
    }

    public ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default)
    {
        var constructor = _registry.Resolve(descriptorType);
        return constructor.Construct(payload, inputs, outputs, cancellationToken);
    }
}
