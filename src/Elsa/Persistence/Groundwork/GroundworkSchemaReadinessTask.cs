using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Scoping;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Startup readiness validation for a Groundwork-backed composition. It runs in the
/// <see cref="LifecyclePhase.Start"/> phase, strictly after every provider admission initializer,
/// and verifies that an admitted provider publication exists and that it carries explicit
/// transaction-boundary evidence. It never applies schema, never repairs state, and never selects
/// a fallback store: any missing precondition is a blocking
/// <see cref="GroundworkSchemaReadinessException"/> so traffic is not served over an unvalidated
/// composition. Missing, stale, or drifted applied schema is diagnosed by the provider admission
/// itself with the exact incompatibility; this guard closes the remaining gap where no admission
/// published a validated provider at all.
/// </summary>
public sealed class GroundworkSchemaReadinessTask(GroundworkStoreSessionSource sessionSource) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!sessionSource.IsInitialized)
        {
            throw new GroundworkSchemaReadinessException(
                "No admitted Groundwork provider publication is available. Readiness validation never applies " +
                "schema and never selects a fallback store; verify that exactly one provider leaf is selected " +
                "and that its applied schema admitted successfully.");
        }

        if (sessionSource.AdmittedTransactionBoundary is null)
        {
            throw new GroundworkSchemaReadinessException(
                "The Groundwork provider publication carries no admitted transaction-boundary evidence. " +
                "A legacy publication without durability evidence must not serve traffic; re-admit the " +
                "provider through its schema-validating initializer.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>A blocking startup readiness failure of a Groundwork-backed composition.</summary>
public sealed class GroundworkSchemaReadinessException(string message) : InvalidOperationException(message);
