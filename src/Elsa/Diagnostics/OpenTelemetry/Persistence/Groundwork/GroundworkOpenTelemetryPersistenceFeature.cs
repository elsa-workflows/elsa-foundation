using CShells.Features;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Selects the first-party Groundwork OpenTelemetry persistence adapter and contributes its four
/// immutable signal streams to the shared diagnostics deployment.
/// </summary>
public class GroundworkOpenTelemetryPersistenceFeature :
    IShellFeature,
    IGroundworkDiagnosticRecordManifestSource
{
    public const string FeatureName = "diagnostics-open-telemetry-groundwork";

    public string FeatureIdentity => FeatureName;

    public IReadOnlyList<DiagnosticRecordStreamDefinition> CreateDiagnosticRecordStreams() =>
        OpenTelemetryGroundworkStorageSchema.CreateStreams(GroundworkOpenTelemetryBinding.Default);

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(GroundworkOpenTelemetryBinding.Default);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkOpenTelemetryStore)))
        {
            // The default feature registers its concrete fallback independently of the contract. Remove it
            // by its established type identity without taking a compile-time dependency on that adapter.
            foreach (var fallback in services
                         .Where(descriptor => descriptor.ServiceType.FullName ==
                             "Elsa.Diagnostics.OpenTelemetry.Providers.InMemory.InMemoryOpenTelemetryStore")
                         .ToArray())
            {
                services.Remove(fallback);
            }
            services.ReplaceDiagnosticsStore<
                IOpenTelemetryStore,
                GroundworkOpenTelemetryStore>(ServiceLifetime.Singleton);
        }
        services.AddDiagnosticsPersistenceLifecycle<GroundworkOpenTelemetryStore>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IGroundworkDiagnosticRecordManifestSource,
            GroundworkOpenTelemetryPersistenceFeature>());
    }
}
