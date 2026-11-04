using Elsa.Activities.Runtime.Core.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// The activity state view projectors the shell composed, keyed by the private-state type alias they read, and optionally by
/// the cleared-state activity type and the executable-node descriptor kind they read. At most one projector per key: a
/// duplicate fails construction, which <see cref="ValidateActivityStateViewProjectorsStartupTask"/> forces at startup.
/// </summary>
public sealed class ActivityStateViewProjectorRegistry
{
    private readonly IReadOnlyDictionary<string, IActivityStateViewProjector> _projectors;
    private readonly IReadOnlyDictionary<string, IActivityStateViewProjector> _withoutState;
    private readonly IReadOnlyDictionary<string, IActivityStateViewProjector> _byDescriptor;

    public ActivityStateViewProjectorRegistry(IEnumerable<IActivityStateViewProjector> projectors)
    {
        ArgumentNullException.ThrowIfNull(projectors);
        var byAlias = new Dictionary<string, IActivityStateViewProjector>(StringComparer.Ordinal);
        var withoutState = new Dictionary<string, IActivityStateViewProjector>(StringComparer.Ordinal);
        var byDescriptor = new Dictionary<string, IActivityStateViewProjector>(StringComparer.Ordinal);
        foreach (var projector in projectors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projector.StateTypeAlias);
            if (!byAlias.TryAdd(projector.StateTypeAlias, projector))
                throw new InvalidOperationException(
                    $"Activity state view projectors '{byAlias[projector.StateTypeAlias].GetType().FullName}' and '{projector.GetType().FullName}' both project private state '{projector.StateTypeAlias}'; at most one may.");
            if (projector.ActivityTypeWithoutState is { } activityType)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
                if (!withoutState.TryAdd(activityType, projector))
                    throw new InvalidOperationException($"More than one activity state view projector reads retained references for activity type '{activityType}'; at most one may.");
            }
            if (projector.DescriptorKind is { } descriptorKind)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(descriptorKind);
                // Every CLR, graph or intrinsic node shares one of these kinds, so claiming one would silently shadow the alias
                // and cleared-type projectors of all of them.
                if (SharedDescriptorKinds.Contains(descriptorKind))
                    throw new InvalidOperationException($"Activity state view projector '{projector.GetType().FullName}' claims descriptor kind '{descriptorKind}', which a whole activity family shares; project those activities by private-state alias or activity type instead.");
                if (!byDescriptor.TryAdd(descriptorKind, projector))
                    throw new InvalidOperationException($"More than one activity state view projector reads executable nodes of descriptor kind '{descriptorKind}'; at most one may.");
            }
        }

        _projectors = byAlias;
        _withoutState = withoutState;
        _byDescriptor = byDescriptor;
    }

    /// <summary>Whether any projector is composed, which decides whether the state view link is advertised.</summary>
    public bool HasProjectors => _projectors.Count > 0;

    /// <summary>Whether a projector reads by descriptor kind, which requires the pinned node before a projector is chosen.</summary>
    internal bool HasDescriptorProjectors => _byDescriptor.Count > 0;

    private static readonly HashSet<string> SharedDescriptorKinds = new(StringComparer.Ordinal)
    {
        WellKnownRuntimeActivityConsumers.ClrActivity, WellKnownRuntimeActivityConsumers.GraphActivity, WellKnownRuntimeActivityConsumers.Intrinsic
    };

    public IActivityStateViewProjector? Find(string stateTypeAlias) => _projectors.GetValueOrDefault(stateTypeAlias);

    internal IActivityStateViewProjector? FindWithoutState(string activityType) => _withoutState.GetValueOrDefault(activityType);
    internal IActivityStateViewProjector? FindByDescriptor(string? descriptorKind) => descriptorKind is null ? null : _byDescriptor.GetValueOrDefault(descriptorKind);
}

/// <summary>Builds the projector registry during startup so a duplicate alias fails shell activation.</summary>
public sealed class ValidateActivityStateViewProjectorsStartupTask : IStartupTask
{
    public ValidateActivityStateViewProjectorsStartupTask(ActivityStateViewProjectorRegistry registry) =>
        ArgumentNullException.ThrowIfNull(registry);

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
