using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral boundary for the shared design-persistence scenarios. Provider fixtures own
/// materialization and lifecycle details; scenarios receive only a scoped service provider and
/// explicit restart/readiness operations.
/// </summary>
public interface IDesignPersistenceContractFixture : IAsyncDisposable
{
    /// <summary>The provider identity written into scenario evidence, for example <c>sqlite</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// Creates a fresh scope-bound service provider for one ordinary design request. The returned
    /// provider is bound to <paramref name="storageScope"/> for every resolved design store and
    /// command; implementations must not reuse a provider or an access context from another scope.
    /// </summary>
    IServiceScope CreateScope(string storageScope);

    /// <summary>Closes and reopens the same durable target without changing its contents.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the selected schema and provider capabilities without applying changes.</summary>
    Task ValidateReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the supplied candidates visible to the fixture's normal reconciliation source.
    /// This method MUST NOT persist candidates or invoke reconciliation; the shared suite verifies
    /// pre-reconciliation absence and invokes the real domain reconciler from the request scope.
    /// </summary>
    Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the event/outcome observation window without changing durable state.</summary>
    void ClearObservedEvents();

    /// <summary>
    /// Returns events emitted through the composed domain event infrastructure since the last
    /// clear. Implementations wait for already-published deferred events to become observable.
    /// </summary>
    Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates one durable fixture per mandatory provider without exposing provider SDK types to scenarios.</summary>
public interface IDesignPersistenceContractFixtureFactory
{
    string Provider { get; }

    Task<IDesignPersistenceContractFixture> CreateAsync(CancellationToken cancellationToken = default);
}
