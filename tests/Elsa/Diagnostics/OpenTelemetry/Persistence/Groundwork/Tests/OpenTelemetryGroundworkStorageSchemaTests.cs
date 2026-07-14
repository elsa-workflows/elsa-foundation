using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Scoping;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
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
            x => x.Identity == OpenTelemetryGroundworkStorageSchema.ByResourceStatusIndex);
        Assert.Contains(resources.PhysicalStorage.BoundedQueries,
            x => x.Identity == "resources-by-last-seen" && x.ExecutionClass == BoundedQueryExecutionClass.ScaleBearing);
        var resourcePolicy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(resources.PhysicalStorage.Policy);
        Assert.Contains(resourcePolicy.Definition.Indexes,
            x => x.LogicalName == OpenTelemetryGroundworkStorageSchema.ByRetentionIndex && x.Columns.Count == 3);
        Assert.Contains("open-telemetry-capture-operation", OpenTelemetryGroundworkStorageSchema.RequiredOperationLedgers);
    }

    [Fact]
    public void Schema_resolves_compiles_and_passes_the_sql_server_physical_store_validator_without_io()
    {
        var binding = GroundworkOpenTelemetryBinding.Create("tenant", "scope", "source");
        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            SqlServerGroundworkCapabilities.PhysicalNames);

        Assert.True(resolution.IsValid, string.Join("; ", resolution.Diagnostics.Select(x => x.Message)));
        var compilation = ExecutableStorageRouteCompiler.Compile(resolution.Definitions);
        Assert.True(compilation.IsValid, string.Join("; ", compilation.Diagnostics.Select(x => x.Message)));

        var store = new SqlServerPhysicalDocumentStore(
            "Server=localhost;Database=groundwork;User ID=sa;Password=not-used;Encrypt=False",
            manifest,
            compilation.Routes,
            DocumentStoreAccess.Scoped(binding.DocumentStorageScope));

        Assert.Equal(binding.DocumentStorageScope, store.Access.Scope);
    }

    [Fact]
    public void Every_declared_physical_index_is_within_sql_server_key_width_limits()
    {
        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();

        foreach (var unit in manifest.StorageUnits)
        {
            var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy);
            var columns = policy.Definition.ProjectedColumns.ToDictionary(x => x.LogicalName, StringComparer.Ordinal);
            foreach (var index in policy.Definition.Indexes)
            {
                var keyBytes = index.Columns.Sum(column => column.ColumnLogicalName switch
                {
                    "storage_scope" => 128 * 2,
                    _ when columns[column.ColumnLogicalName].Type == PortablePhysicalType.String =>
                        columns[column.ColumnLogicalName].Length!.Value * 2,
                    _ when columns[column.ColumnLogicalName].Type == PortablePhysicalType.Int32 => 4,
                    _ when columns[column.ColumnLogicalName].Type == PortablePhysicalType.Int64 => 8,
                    _ => throw new Xunit.Sdk.XunitException(
                        $"SQL Server validator does not recognize index column '{column.ColumnLogicalName}'.")
                });

                Assert.True(keyBytes <= 1_700,
                    $"SQL Server physical index '{index.LogicalName}' requires {keyBytes} key bytes; the provider maximum is 1700.");
            }
        }
    }
}
