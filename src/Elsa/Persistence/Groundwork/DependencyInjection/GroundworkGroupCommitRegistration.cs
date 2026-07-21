using Elsa.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Opt-in wiring for cross-drain group commit on the shared Groundwork durable writer (spec 115). When registered, the
/// <see cref="GroundworkRuntimeCheckpointWriter"/> resolves a process-wide <see cref="RuntimeGroupCommitCoordinator"/>
/// and folds concurrent checkpoint commits that are contending for the single durable writer into one shared
/// unit-of-work / one fsync. A lone committer is never batched and never waits, so solo latency is unchanged; when this
/// is not called the writer keeps its per-commit unit-of-work exactly as before.
/// </summary>
public static class GroundworkGroupCommitRegistration
{
    /// <summary>
    /// Registers the group-commit coordinator and its options. Call after <see cref="GroundworkRuntimeStoreRegistration.AddGroundworkRuntimeStores"/>
    /// so the durable checkpoint writer picks it up.
    /// </summary>
    public static IServiceCollection AddGroundworkRuntimeGroupCommit(
        this IServiceCollection services,
        Action<RuntimeGroupCommitOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RuntimeGroupCommitOptions();
        configureOptions?.Invoke(options);
        if (options.MaxBatchSize < 2)
            throw new ArgumentException($"{nameof(RuntimeGroupCommitOptions.MaxBatchSize)} must be greater than one; a max of one disables batching.", nameof(configureOptions));

        services.RemoveAll<RuntimeGroupCommitOptions>();
        services.AddSingleton(options);
        services.TryAddSingleton<RuntimeGroupCommitCoordinator>();
        return services;
    }
}
