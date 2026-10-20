using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// The activity state view projectors the shell composed, keyed by the private-state type alias they read. At most one
/// projector per alias: a duplicate fails construction, which <see cref="ValidateActivityStateViewProjectorsStartupTask"/>
/// forces at startup.
/// </summary>
public sealed class ActivityStateViewProjectorRegistry
{
    private readonly IReadOnlyDictionary<string, IActivityStateViewProjector> _projectors;

    public ActivityStateViewProjectorRegistry(IEnumerable<IActivityStateViewProjector> projectors)
    {
        ArgumentNullException.ThrowIfNull(projectors);
        var byAlias = new Dictionary<string, IActivityStateViewProjector>(StringComparer.Ordinal);
        foreach (var projector in projectors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projector.StateTypeAlias);
            if (!byAlias.TryAdd(projector.StateTypeAlias, projector))
                throw new InvalidOperationException(
                    $"Activity state view projectors '{byAlias[projector.StateTypeAlias].GetType().FullName}' and '{projector.GetType().FullName}' both project private state '{projector.StateTypeAlias}'; at most one may.");
        }

        _projectors = byAlias;
    }

    /// <summary>Whether any projector is composed, which decides whether the state view link is advertised.</summary>
    public bool HasProjectors => _projectors.Count > 0;

    public IActivityStateViewProjector? Find(string stateTypeAlias) => _projectors.GetValueOrDefault(stateTypeAlias);
}

/// <summary>Builds the projector registry during startup so a duplicate alias fails shell activation.</summary>
public sealed class ValidateActivityStateViewProjectorsStartupTask : IStartupTask
{
    public ValidateActivityStateViewProjectorsStartupTask(ActivityStateViewProjectorRegistry registry) =>
        ArgumentNullException.ThrowIfNull(registry);

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
