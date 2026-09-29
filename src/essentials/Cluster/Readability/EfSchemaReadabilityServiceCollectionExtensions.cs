using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Cluster.Readability;

public static class EfSchemaReadabilityServiceCollectionExtensions
{
    /// <summary>
    /// Reports the schema families this host has loaded through its cluster membership (spec 183, FR-019 to FR-022).
    /// Compose it once, on the host container and never per shell, so every shell publishes the same report (FR-017;
    /// Decisions, Q20). It also registers the in-process membership default unless a provider is already registered, so
    /// a host that is not clustered reports its readability without composing anything else, while a durable provider
    /// composed in either order replaces the default and reads the same source.
    /// </summary>
    /// <remarks>
    /// It is also the membership side of the schema finalization gate (spec 181): every EF module's gate counts the fleet
    /// through <see cref="ClusterSchemaFleet"/>, and records what it read in the host's one
    /// <see cref="EfSchemaFinalizationObservations"/>, registered here by instance so every shell container built from
    /// copies of the host's registrations shares it, and this report names what the host's gates read. A host that
    /// composes neither has gates that never finalize a version past the one each record was created at.
    /// <para>
    /// It also composes the host's <see cref="ISupersededAssemblySource"/>, <see cref="NuplanePackageGenerations"/>, unless
    /// one is already registered (spec 183, FR-021, amended 2026-09-29): by instance, so every shell shares it, and bound
    /// to the host's own container through the CShells lifecycle subscriber registered beside it, which CShells builds
    /// from the host's container alone. On a host without Nuplane, or without CShells, it never names anything superseded,
    /// and the report reads every load context as before.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEfSchemaReadability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new EfSchemaFinalizationObservations());
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ISupersededAssemblySource)))
        {
            var generations = new NuplanePackageGenerations();
            services.AddSingleton<ISupersededAssemblySource>(generations);
            services.AddSingleton<IShellLifecycleSubscriber>(host => generations.BindTo(host));
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<ReadabilitySection>, EfSchemaReadabilitySource>());
        services.TryAddSingleton<IEfSchemaFleet, ClusterSchemaFleet>();
        services.AddEfSchemaDormancy();
        return services.TryAddInProcessClusterMembership();
    }

    /// <summary>
    /// Composes the shared dormancy check over what this container's EF finalization gates observe (spec 182, FR-003), so
    /// a feature that needs data only a newer schema version holds stays dormant until its host observes that version as
    /// finalized. <see cref="AddEfSchemaReadability"/> composes it; a host that composes no membership calls this alone.
    /// </summary>
    /// <remarks>
    /// Both are registered by type, never by instance, so every shell container built from copies of the host's
    /// registrations gets its own, reading the gates of the modules that shell admitted.
    /// </remarks>
    public static IServiceCollection AddEfSchemaDormancy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.TryAddSchemaDormancyCheck().AddObservedSchemaFinalization<EfObservedSchemaFinalization>();
    }
}
