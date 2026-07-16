using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Pure dispatch / lifecycle orchestrator. Resolves the constructor registered for the stable
/// consumer/schema pair and delegates construction. Startup owns registry population.
/// No type resolution or argument binding lives here — those are kind-specific and owned by
/// each <see cref="IActivityConstructor"/>.
/// </summary>
public sealed class ActivityFactory : IActivityFactory
{
    private readonly IActivityConstructorRegistry _registry;

    public ActivityFactory(IActivityConstructorRegistry registry) => _registry = registry;

    public ValueTask<IActivity> Create(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument>? inputs,
        IReadOnlyDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var constructor = _registry.Resolve(descriptor);
        return constructor.ConstructAsync(
            descriptor,
            inputs ?? new Dictionary<string, InputArgument>(),
            outputs ?? new Dictionary<string, OutputArgument>(),
            cancellationToken);
    }
}
