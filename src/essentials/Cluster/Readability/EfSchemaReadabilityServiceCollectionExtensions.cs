using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
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
    public static IServiceCollection AddEfSchemaReadability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<ReadabilitySection>, EfSchemaReadabilitySource>());
        return services.TryAddInProcessClusterMembership();
    }
}
