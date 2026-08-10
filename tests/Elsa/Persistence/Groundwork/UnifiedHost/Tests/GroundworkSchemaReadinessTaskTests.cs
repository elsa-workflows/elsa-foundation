using CShells.Lifecycle;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// T060/T061: direct branch coverage for the startup readiness guard. The guard must block traffic
/// on every unvalidated composition shape and must never apply schema or select a fallback. Every
/// declared target is validated, so a host that admits one target and misses another still fails.
/// </summary>
public sealed class GroundworkSchemaReadinessTaskTests
{
    private static Func<DocumentStoreAccess, CancellationToken, ValueTask<GroundworkStoreSessionResources>> Session =>
        (_, _) => throw new InvalidOperationException("The readiness guard must not open provider sessions.");

    /// <summary>Builds a guard over the given per-target session sources, declaring each target.</summary>
    private static GroundworkSchemaReadinessTask GuardOver(
        params (string Target, GroundworkStoreSessionSource Source)[] targets)
    {
        var registry = new GroundworkTargetRegistry();
        var services = new ServiceCollection();
        foreach (var (target, source) in targets)
        {
            registry.Declare(GroundworkTargetDeclaration.Create(target, "test", $"Data Source={target}"));
            services.AddKeyedSingleton(target, source);
        }

        return new GroundworkSchemaReadinessTask(registry, services.BuildServiceProvider());
    }

    private static GroundworkSchemaReadinessTask GuardOver(GroundworkStoreSessionSource source) =>
        GuardOver((GroundworkTargetNames.Default, source));

    [Fact]
    public async Task Missing_provider_publication_is_a_blocking_readiness_failure()
    {
        await using var source = new GroundworkStoreSessionSource();
        var task = GuardOver(source);

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("never applies schema", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{GroundworkTargetNames.Default}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_publication_without_transaction_boundary_evidence_is_rejected()
    {
        await using var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySet(Session));
        var task = GuardOver(source);

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("transaction-boundary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admitted_publication_with_boundary_evidence_passes_without_provider_io()
    {
        await using var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(Session, TransactionBoundary.CrossUnitAtomic));
        var task = GuardOver(source);

        await task.InitializeAsync();
    }

    [Fact]
    public async Task Every_declared_target_is_validated_not_just_the_first()
    {
        await using var admitted = new GroundworkStoreSessionSource();
        Assert.True(admitted.TrySetAdmitted(Session, TransactionBoundary.CrossUnitAtomic));
        await using var unadmitted = new GroundworkStoreSessionSource();
        var task = GuardOver(("authoring", admitted), ("runtime", unadmitted));

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("'runtime'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_composition_that_declares_no_target_is_a_blocking_readiness_failure()
    {
        var task = GuardOver([]);

        var exception = await Assert.ThrowsAsync<GroundworkSchemaReadinessException>(() => task.InitializeAsync());

        Assert.Contains("No Groundwork target is declared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_any_validation()
    {
        await using var source = new GroundworkStoreSessionSource();
        var task = GuardOver(source);
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
