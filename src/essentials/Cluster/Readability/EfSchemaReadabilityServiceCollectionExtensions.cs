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
    /// one is already registered (spec 183, FR-021, amended 2026-09-29): by instance, so every shell shares it, bound to
    /// the host's own container through the CShells lifecycle subscriber registered beside it, which CShells builds from
    /// the host's container alone, and fed by the shell initializer registered beside that, which every shell container
    /// copies and constructs before its first initializer runs, so each shell generation is counted from then until its
    /// container has finished disposing. On a host without Nuplane, or without CShells, it never names anything
    /// superseded, and the report reads every load context as before.
    /// </para>
    /// <para>
    /// The host's <see cref="IEfSchemaFleet"/> (through <see cref="ShellServiceSharingExtensions.ShareWithShells{TService}"/>)
    /// and, unless a durable provider replaces it, its in-process <see cref="IClusterMembership"/> (as the EF provider's
    /// member is) are one instance each too, shared with every shell container: a gate counting the fleet in a shell asks the
    /// membership the host publishes through, not a second one of the shell's own. The host-composition guard test in
    /// <c>Elsa.Modularity.Tests</c> holds that for both real hosts.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEfSchemaReadability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new EfSchemaFinalizationObservations());
        var generations = new NuplanePackageGenerations();
        services.TryAddSingleton<ISupersededAssemblySource>(generations);
        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationInstance == generations))
        {
            services.AddSingleton<IShellLifecycleSubscriber>(host => generations.BindTo(host));
            services.AddSingleton<IShellInitializer>(container => generations.Track(container));
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<ReadabilitySection>, EfSchemaReadabilitySource>());
        // The host's one fleet, which is what its own membership answers for: a shell's copy would be a second instance of it.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IEfSchemaFleet)))
            services.AddSingleton<IEfSchemaFleet, ClusterSchemaFleet>().ShareWithShells<IEfSchemaFleet>();
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
