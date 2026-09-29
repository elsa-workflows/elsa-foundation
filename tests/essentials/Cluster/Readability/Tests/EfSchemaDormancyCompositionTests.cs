using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Readability;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 182, FR-003: a host that composes readability gets the shared dormancy check over its EF finalization gates, and
/// a host that composes no membership can compose the check alone. Both are registered by type, so each shell container
/// built from copies of the host's registrations reads its own gates.
/// </summary>
public sealed class EfSchemaDormancyCompositionTests
{
    [Fact]
    public void Readability_composes_the_shared_dormancy_check_over_the_containers_finalization_gates()
    {
        using var host = new ServiceCollection().AddEfSchemaReadability().AddEfSchemaReadability().BuildServiceProvider();

        Assert.IsType<SchemaDormancyCheck>(host.GetRequiredService<ISchemaDormancyCheck>());
        Assert.IsType<EfObservedSchemaFinalization>(host.GetRequiredService<IObservedSchemaFinalization>());
        Assert.Empty(host.GetRequiredService<ISchemaDormancyCheck>().Observe());
    }

    [Fact]
    public void The_check_composes_alone_without_membership()
    {
        var services = new ServiceCollection().AddEfSchemaDormancy();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IClusterMembership));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IObservedSchemaFinalization));
    }

    [Fact]
    public void Every_container_built_from_the_same_registrations_gets_a_source_of_its_own()
    {
        var services = new ServiceCollection().AddEfSchemaDormancy();
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();

        Assert.NotSame(first.GetRequiredService<IObservedSchemaFinalization>(), second.GetRequiredService<IObservedSchemaFinalization>());
        Assert.NotSame(first.GetRequiredService<ISchemaDormancyCheck>(), second.GetRequiredService<ISchemaDormancyCheck>());
    }
}
