using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Groundwork.Tests;

public sealed class DiagnosticsGroundworkV2FeatureTests
{
    [Fact]
    public void AggregateFeatureSelectsBothCleanBreakV2StoresWithoutALegacyManifest()
    {
        var services = new ServiceCollection();

        new DiagnosticsGroundworkPersistenceFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOpenTelemetryStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStructuredLogStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkOpenTelemetryStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName is
                "Elsa.Persistence.Groundwork.Composition.IGroundworkStorageManifestSource" or
                "Groundwork.DiagnosticRecords.IDiagnosticRecordDeploymentManifestSource");
    }

    [Fact]
    public void AggregateAssemblyHasNoV1GroundworkPackageClosureOrCompatibilityTypes()
    {
        var assembly = typeof(DiagnosticsGroundworkPersistenceFeature).Assembly;
        var references = assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is
            "Groundwork.Core" or "Groundwork.Documents" or "Groundwork.DiagnosticRecords");
        Assert.Null(assembly.GetType("Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkDeploymentSchema"));
        Assert.Null(assembly.GetType("Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkStorageManifest"));
        Assert.Null(assembly.GetType("Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkStorageManifestSource"));
    }
}
