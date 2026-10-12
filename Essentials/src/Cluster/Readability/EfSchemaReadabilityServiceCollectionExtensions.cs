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
    /// one is already registered. The nondisposable source is shared by instance with shell providers. A separate
    /// root-only <see cref="NuplanePackageGenerationBuildParticipant"/> acquires protection before catalog access and
    /// retains the exact selected features until CShells confirms teardown. The lifecycle factory starts that same
    /// canonical adapter before binding the source to the host's catalogs and membership. Root disposal detaches catalog
    /// notifications and stops its custom-catalog watch; shell disposal cannot dispose the shared source. A host without
    /// Nuplane has no positive replacement evidence. A host without CShells has no shell-generation pins.
    /// </para>
    /// <para>
    /// The host's <see cref="IEfSchemaFleet"/> is one instance for the whole host, shared with every shell container through
    /// <see cref="ShellServiceSharingExtensions.ShareWithShells{TService}"/>: it is always built in the host's container, so it
    /// counts through the membership the host publishes through, and a gate in a shell asks that one, not a second fleet of the
    /// shell's own. The in-process <see cref="IClusterMembership"/> is not shared: each shell container builds its own, because a
    /// shell's runnability source publishes through the member of its own container. The host-composition guard test in
    /// <c>Elsa.Modularity.Tests</c> holds both for the real hosts.
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
            services.AddSingleton<NuplanePackageGenerationBuildParticipant>(root => new(generations, root));
            services.AddSingleton<IShellGenerationBuildParticipant>(root => root.GetRequiredService<NuplanePackageGenerationBuildParticipant>());
            services.AddSingleton<IShellLifecycleSubscriber>(root =>
            {
                root.GetRequiredService<NuplanePackageGenerationBuildParticipant>().EnsureStarted();
                return generations.BindTo(root);
            });
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
