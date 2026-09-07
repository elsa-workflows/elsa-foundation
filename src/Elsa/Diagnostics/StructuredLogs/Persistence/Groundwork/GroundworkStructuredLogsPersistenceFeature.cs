using CShells.Features;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>
/// Selects the first-party Groundwork v2 Structured Logs persistence adapter. Its storage unit is
/// applied and admitted through the v2 provider connection at diagnostics startup; it is not part of
/// the legacy diagnostic-record deployment.
/// </summary>
public class GroundworkStructuredLogsPersistenceFeature :
    IShellFeature
{
    public const string FeatureName = "diagnostics-structured-logs-groundwork";

    public string FeatureIdentity => FeatureName;

    /// <summary>
    /// Optional Groundwork target for this store's provider connection; null shares the default connection.
    /// </summary>
    public string? Target { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var target = Target;
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(StructuredLogStoreBinding.Default);
        services.RemoveAll<InMemoryStructuredLogStore>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore)))
        {
            services.ReplaceDiagnosticsStore<IStructuredLogStore, GroundworkStructuredLogStore>(ServiceLifetime.Singleton);
            services.Replace(ServiceDescriptor.Singleton<GroundworkStructuredLogStore>(serviceProvider =>
                new GroundworkStructuredLogStore(
                    GroundworkProviderConnections.Resolve(serviceProvider, target, "Diagnostics structured-log persistence"),
                    serviceProvider.GetRequiredService<IOptions<StructuredLogsOptions>>(),
                    serviceProvider.GetRequiredService<StructuredLogStoreBinding>(),
                    observer: serviceProvider.GetService<Elsa.Diagnostics.Persistence.Observability.IDiagnosticsPersistenceObserver>(),
                    commandObserver: serviceProvider.GetService<IProviderCommandObserver>())));
        }
        services.AddDiagnosticsPersistenceLifecycle<GroundworkStructuredLogStore>();
    }
}
