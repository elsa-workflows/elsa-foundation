using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class OpenTelemetryGroundworkStorageSchemaTests
{
    [Fact]
    public void Schema_declares_explicit_streams_dedicated_catalog_tables_indexes_and_capture_ledger()
    {
        var binding = GroundworkOpenTelemetryBinding.Create("tenant", "scope", "source");
        var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(binding);
        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();

        Assert.Equal(
            new[] { binding.TraceStreamId, binding.SpanStreamId, binding.MetricPointStreamId, binding.LogStreamId },
            streams.Select(x => x.Stream.Value));
        Assert.Equal(
            new[]
            {
                CatalogDocuments.ResourceKind,
                CatalogDocuments.InstrumentKind,
                OpenTelemetryGroundworkStorageSchema.OperationLedgerKind
            }.Order(StringComparer.Ordinal),
            manifest.StorageUnits.Select(x => x.Identity.Value).Order(StringComparer.Ordinal));
        Assert.All(manifest.StorageUnits, unit => Assert.Equal(TenancyPolicy.Scoped, unit.Tenancy));
        Assert.All(manifest.StorageUnits, unit =>
        {
            var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy);
            Assert.Equal(PhysicalStorageForm.DedicatedDocumentTable, policy.Definition.Form);
        });

        var resources = manifest.StorageUnits.Single(x => x.Identity.Value == CatalogDocuments.ResourceKind);
        Assert.Contains(resources.PhysicalStorage!.LogicalIndexes,
            x => x.Identity == OpenTelemetryGroundworkStorageSchema.ByServiceNameIndex);
        Assert.Contains(resources.PhysicalStorage.BoundedQueries,
            x => x.Identity == "resources-by-last-seen" && x.ExecutionClass == BoundedQueryExecutionClass.ScaleBearing);
        var resourcePolicy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(resources.PhysicalStorage.Policy);
        Assert.Contains(resourcePolicy.Definition.Indexes,
            x => x.LogicalName == OpenTelemetryGroundworkStorageSchema.ByRetentionIndex && x.Columns.Count == 3);
        Assert.Contains("open-telemetry-capture-operation", OpenTelemetryGroundworkStorageSchema.RequiredOperationLedgers);
    }
}
