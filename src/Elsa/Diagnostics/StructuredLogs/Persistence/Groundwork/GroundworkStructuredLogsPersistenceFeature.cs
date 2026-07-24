using CShells.Features;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>
/// Selects the first-party Groundwork Structured Logs persistence adapter and contributes its immutable
/// history stream to the shared diagnostics deployment.
/// </summary>
public class GroundworkStructuredLogsPersistenceFeature :
    IShellFeature,
    IGroundworkDiagnosticRecordManifestSource
{
    public const string FeatureName = "diagnostics-structured-logs-groundwork";

    public string FeatureIdentity => FeatureName;

    public IReadOnlyList<DiagnosticRecordStreamDefinition> CreateDiagnosticRecordStreams() =>
        [GroundworkStructuredLogStore.StreamDefinition];

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore)))
        {
            services.TryAddSingleton(StructuredLogStoreBinding.Default);
            services.RemoveAll<InMemoryStructuredLogStore>();
            services.ReplaceDiagnosticsStore<
                IStructuredLogStore,
                GroundworkStructuredLogStore>(ServiceLifetime.Singleton);
        }
        services.AddDiagnosticsPersistenceLifecycle<GroundworkStructuredLogStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IGroundworkDiagnosticRecordManifestSource,
            GroundworkStructuredLogsPersistenceFeature>());
    }
}
