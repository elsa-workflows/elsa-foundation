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
public sealed class ActivityFactory(
    IActivityConstructorRegistry registry,
    IEnumerable<IActivityConstructor>? constructors = null)
    : IActivityFactory
{
    private readonly Lock _constructorRegistrationGate = new();
    private bool _constructorsRegistered;

    public ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default)
    {
        EnsureContributedConstructorsRegistered();

        var constructor = registry.Resolve(descriptorType);
        return constructor.Construct(payload, inputs, outputs, cancellationToken);
    }

    private void EnsureContributedConstructorsRegistered()
    {
        if (_constructorsRegistered)
            return;

        lock (_constructorRegistrationGate)
        {
            if (_constructorsRegistered)
                return;

            registry.AddAll(constructors ?? []);
            _constructorsRegistered = true;
        }
    }
}
