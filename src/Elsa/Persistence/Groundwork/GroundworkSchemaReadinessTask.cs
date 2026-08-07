using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Startup readiness validation for a Groundwork-backed composition. It runs in the
/// <see cref="LifecyclePhase.Start"/> phase, strictly after every provider admission, and verifies for
/// <em>every declared target</em> that an admitted provider publication exists and that it carries explicit
/// transaction-boundary evidence. It never applies schema, never repairs state, and never selects a fallback
/// store: any missing precondition is a blocking <see cref="GroundworkSchemaReadinessException"/> so traffic
/// is not served over an unvalidated composition. Missing, stale, or drifted applied schema is diagnosed by
/// the provider admission itself with the exact incompatibility; this guard closes the remaining gap where a
/// declared target got no admitted publication at all.
/// </summary>
public sealed class GroundworkSchemaReadinessTask(
    GroundworkTargetRegistry targets,
    IServiceProvider serviceProvider,
    GroundworkTargetComposition? composition = null) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEveryConfiguredTargetFoundItsProvider();
        var declared = targets.TargetNames;
        if (declared.Count == 0)
        {
            throw new GroundworkSchemaReadinessException(
                "No Groundwork target is declared, so no persistence store can be resolved. Readiness " +
                "validation never applies schema and never selects a fallback store; compose a provider " +
                "feature that declares at least one target.");
        }

        foreach (var target in declared)
        {
            var sessionSource = serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(target);
            if (!sessionSource.IsInitialized)
            {
                throw new GroundworkSchemaReadinessException(
                    $"Groundwork target '{target}' has no admitted provider publication. Readiness validation " +
                    "never applies schema and never selects a fallback store; verify that the target's provider " +
                    "leaf is composed and that its applied schema admitted successfully.");
            }

            if (sessionSource.AdmittedTransactionBoundary is null)
            {
                throw new GroundworkSchemaReadinessException(
                    $"The Groundwork publication for target '{target}' carries no admitted transaction-boundary " +
                    "evidence. A publication without durability evidence must not serve traffic; re-admit the " +
                    "target through its schema-validating initializer.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// A target naming a provider whose package was never composed produces no store at all. Nothing else
    /// notices, because admission only walks the targets that were declared, so the configuration would
    /// simply have no effect. Name it here instead.
    /// </summary>
    private void EnsureEveryConfiguredTargetFoundItsProvider()
    {
        var pending = composition?.PendingTargets ?? [];
        if (pending.Count == 0)
            return;

        var composed = composition!.ComposedProviders;
        var available = composed.Count == 0
            ? "no Groundwork provider leaf is composed"
            : $"composed providers are {string.Join(", ", composed.Select(provider => $"'{provider}'"))}";
        var descriptions = pending.Select(target =>
            $"'{target}' (provider '{composition.RequestedProvider(target) ?? "<unset>"}')");
        throw new GroundworkSchemaReadinessException(
            $"Groundwork target(s) {string.Join(", ", descriptions)} name a provider that is not composed; " +
            $"{available}. Enable the provider package's shell feature, or correct the target's provider.");
    }
}

/// <summary>A blocking startup readiness failure of a Groundwork-backed composition.</summary>
public sealed class GroundworkSchemaReadinessException(string message) : InvalidOperationException(message);
