using CShells.Features;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.Persistence.Extensions;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Selects the clean-break Groundwork v2 OpenTelemetry adapter. The adapter owns ordinary v2
/// storage units and is admitted through the shared provider connection at diagnostics startup.
/// </summary>
public class GroundworkOpenTelemetryPersistenceFeature : IShellFeature
{
    public const string FeatureName = "diagnostics-open-telemetry-groundwork";

    public string FeatureIdentity => FeatureName;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(GroundworkOpenTelemetryBinding.Default);
        services.TryAddSingleton<V2OpenTelemetryBinding>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkOpenTelemetryBinding>().ToV2Binding());
        foreach (var fallback in services
                     .Where(descriptor => descriptor.ServiceType.FullName ==
                         "Elsa.Diagnostics.OpenTelemetry.Providers.InMemory.InMemoryOpenTelemetryStore")
                     .ToArray())
            services.Remove(fallback);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkOpenTelemetryStore)))
        {
            services.ReplaceDiagnosticsStore<IOpenTelemetryStore, GroundworkOpenTelemetryStore>(ServiceLifetime.Singleton);
            services.Replace(ServiceDescriptor.Singleton<GroundworkOpenTelemetryStore>(serviceProvider =>
                new GroundworkOpenTelemetryStore(
                    serviceProvider.GetRequiredService<IStorageProviderConnection>(),
                    serviceProvider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>(),
                    serviceProvider.GetRequiredService<V2OpenTelemetryBinding>(),
                    sourceRegistry: serviceProvider.GetService<IOpenTelemetrySourceRegistry>(),
                    observer: serviceProvider.GetService<Elsa.Diagnostics.Persistence.Observability.IDiagnosticsPersistenceObserver>(),
                    commandObserver: serviceProvider.GetService<IProviderCommandObserver>())));
        }
        services.AddDiagnosticsPersistenceLifecycle<GroundworkOpenTelemetryStore>();
    }
}
