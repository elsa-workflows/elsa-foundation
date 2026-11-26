using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Cluster.Core.Extensions;

public static class RunnabilityReportServiceCollectionExtensions
{
    /// <summary>
    /// Registers one runnability registry shared by the host and every shell. Compose this on the host, beside membership.
    /// </summary>
    /// <remarks>Each host composes a single runnability source. Use this helper for package-based runtimes, or the distributed runtime's own registration for compiled hosts.</remarks>
    public static IServiceCollection AddSharedRunnabilityReport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new RunnabilityRegistry());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<RunnabilitySection>, SharedRunnabilityReportSource>());
        return services;
    }

    private sealed class SharedRunnabilityReportSource(RunnabilityRegistry registry) : IMemberReportSource<RunnabilitySection>
    {
        public ValueTask<RunnabilitySection> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RunnabilitySection(registry.Entries));
        }
    }
}
