using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
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
    /// </remarks>
    public static IServiceCollection AddEfSchemaReadability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new EfSchemaFinalizationObservations());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<ReadabilitySection>, EfSchemaReadabilitySource>());
        services.TryAddSingleton<IEfSchemaFleet, ClusterSchemaFleet>();
        return services.TryAddInProcessClusterMembership();
    }
}
