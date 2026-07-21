using CShells.Lifecycle;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// T060/T061: direct branch coverage for the startup readiness guard. The guard must block traffic
/// on every unvalidated composition shape and must never apply schema or select a fallback.
/// </summary>
public sealed class GroundworkSchemaReadinessTaskTests
{
    private static Func<DocumentStoreAccess, CancellationToken, ValueTask<GroundworkStoreSessionResources>> Session =>
        (_, _) => throw new InvalidOperationException("The readiness guard must not open provider sessions.");

    [Fact]
    public async Task Missing_provider_publication_is_a_blocking_readiness_failure()
    {
        await using var source = new GroundworkStoreSessionSource();
        var task = new GroundworkSchemaReadinessTask(source);

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("never applies schema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_publication_without_transaction_boundary_evidence_is_rejected()
    {
        await using var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySet(Session));
        var task = new GroundworkSchemaReadinessTask(source);

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("transaction-boundary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admitted_publication_with_boundary_evidence_passes_without_provider_io()
    {
        await using var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(Session, TransactionBoundary.CrossUnitAtomic));
        var task = new GroundworkSchemaReadinessTask(source);

        await task.InitializeAsync();
    }

    [Fact]
    public async Task Cancellation_is_observed_before_any_validation()
    {
        await using var source = new GroundworkStoreSessionSource();
        var task = new GroundworkSchemaReadinessTask(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task.InitializeAsync(cancellation.Token));
    }

    [Fact]
    public void Guard_registration_is_idempotent_and_targets_the_start_phase()
    {
        var services = new ServiceCollection();

        services.AddGroundworkSchemaReadinessGuard();
        services.AddGroundworkSchemaReadinessGuard();

        Assert.Single(services, d => d.ServiceType == typeof(GroundworkSchemaReadinessTask));
        var registration = Assert.Single(services
            .Where(d => d.ServiceType == typeof(ShellInitializerRegistration))
            .Select(d => d.ImplementationInstance)
            .OfType<ShellInitializerRegistration>()
            .Where(r => r.InitializerType == typeof(GroundworkSchemaReadinessTask)));
        Assert.Equal(LifecyclePhase.Start, registration.Phase);
    }
}
