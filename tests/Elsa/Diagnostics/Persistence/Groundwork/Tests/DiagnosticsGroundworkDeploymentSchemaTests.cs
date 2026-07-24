using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Groundwork.Tests;

public sealed class DiagnosticsGroundworkDeploymentSchemaTests
{
    [Fact]
    public async Task Deployment_source_exposes_the_document_manifest_and_all_diagnostic_streams()
    {
        var declaration = await new DiagnosticsGroundworkStorageManifestSource().CreateDeclarationAsync();
        var source = new DiagnosticsGroundworkDeploymentSchema();
        var deployment = Assert.IsAssignableFrom<IDiagnosticRecordDeploymentManifestSource>(source)
            .CreateDeploymentManifest();

        Assert.Equal(DiagnosticsGroundworkStorageManifestSource.FeatureName, declaration.FeatureIdentity);
        Assert.Equal(
            declaration.Manifest.StorageUnits.Select(unit => unit.Identity.Value).Order(StringComparer.Ordinal),
            deployment.Storage.StorageUnits.Select(unit => unit.Identity.Value).Order(StringComparer.Ordinal));
        Assert.Equal(3, deployment.Storage.StorageUnits.Count);
        Assert.Equal(5, deployment.Streams.Count);
        Assert.All(deployment.Storage.StorageUnits, unit => Assert.NotNull(unit.PhysicalStorage));
        Assert.All(deployment.Streams, DiagnosticRecordStreamDefinitionValidator.ValidateAndThrow);
        Assert.NotNull(source.CreateNamePolicy());
    }

    [Fact]
    public void Record_streams_remain_distinct_from_document_units_in_the_combined_deployment()
    {
        var documentManifest = DiagnosticsGroundworkStorageManifest.CreateDocumentManifest();
        var deployment = DiagnosticsGroundworkStorageManifest.CreateDeploymentManifest();

        Assert.DoesNotContain(documentManifest.StorageUnits, unit =>
            unit.Identity.Value is "structured-logs" or "traces" or "spans" or "metric-points" or "logs");
        Assert.True(documentManifest.HasSameDefinitionAs(deployment.Storage));
        Assert.Equal(5, deployment.Streams.Count);
    }

    [Fact]
    public void Feature_registers_the_manifest_contributor_without_claiming_store_activation()
    {
        var services = new ServiceCollection();

        new DiagnosticsGroundworkPersistenceFeature().ConfigureServices(services);

        var registration = Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(DiagnosticsGroundworkStorageManifestSource)));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName is "Elsa.Diagnostics.OpenTelemetry.Core.Contracts.IOpenTelemetryStore" or
            "Elsa.Diagnostics.StructuredLogs.Core.Contracts.IStructuredLogStore");
    }

    [Fact]
    public void Concrete_features_contribute_their_owned_streams_to_the_shared_deployment()
    {
        IGroundworkDiagnosticRecordManifestSource openTelemetry =
            new GroundworkOpenTelemetryPersistenceFeature();
        IGroundworkDiagnosticRecordManifestSource structuredLogs =
            new GroundworkStructuredLogsPersistenceFeature();
        var deployment = new DiagnosticsGroundworkDeploymentSchema().CreateDeploymentManifest();

        Assert.Equal(4, openTelemetry.CreateDiagnosticRecordStreams().Count);
        Assert.Single(structuredLogs.CreateDiagnosticRecordStreams());
        Assert.Equal(
            openTelemetry.CreateDiagnosticRecordStreams()
                .Concat(structuredLogs.CreateDiagnosticRecordStreams())
                .Select(stream => stream.Stream.Value)
                .Order(StringComparer.Ordinal),
            deployment.Streams.Select(stream => stream.Stream.Value));
    }

    [Fact]
    public void Deployment_schema_is_a_groundwork_tool_compatible_physical_schema_source()
    {
        IPhysicalSchemaManifestSource source = new DiagnosticsGroundworkDeploymentSchema();

        Assert.NotNull(source.CreateManifest());
        Assert.IsAssignableFrom<IPhysicalNamePolicy>(source.CreateNamePolicy());
        Assert.IsAssignableFrom<IDiagnosticRecordDeploymentManifestSource>(source);
    }

    [Fact]
    public void Deployment_registration_uses_the_same_schema_for_runtime_admission()
    {
        var services = new ServiceCollection();

        services.AddGroundworkDiagnosticsDeployment();
        using var provider = services.BuildServiceProvider();

        var source = provider.GetRequiredService<IPhysicalSchemaManifestSource>();
        Assert.IsType<DiagnosticsGroundworkDeploymentSchema>(source);
        Assert.Equal(
            new DiagnosticsGroundworkDeploymentSchema().CreateManifest().StorageUnits.Select(unit => unit.Identity.Value).Order(),
            source.CreateManifest().StorageUnits.Select(unit => unit.Identity.Value).Order());
    }
}
