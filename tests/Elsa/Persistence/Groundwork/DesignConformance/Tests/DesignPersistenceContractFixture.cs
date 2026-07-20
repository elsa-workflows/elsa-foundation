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

    /// <summary>Creates a scope-bound service provider for one ordinary design request.</summary>
    IServiceScope CreateScope(string storageScope);

    /// <summary>Closes and reopens the same durable target without changing its contents.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the selected schema and provider capabilities without applying changes.</summary>
    Task ValidateReadinessAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates one durable fixture per mandatory provider without exposing provider SDK types to scenarios.</summary>
public interface IDesignPersistenceContractFixtureFactory
{
    string Provider { get; }

    Task<IDesignPersistenceContractFixture> CreateAsync(CancellationToken cancellationToken = default);
}
