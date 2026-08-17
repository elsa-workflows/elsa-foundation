using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Groundwork.Kernel;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>Pins the fresh v2 import catalog's physical contract.</summary>
public class Elsa3ImportManifestPhysicalStorageGoldenTests
{
    [Fact]
    public void Manifest_physical_surface_matches_fresh_v2_contract()
    {
        var units = Elsa3ImportStorageManifest.CreateUnits();
        Assert.Equal(3, units.Count);
        Assert.Equal(
            [
                Elsa3ImportStorageManifest.CollectionDocumentKind,
                Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
                Elsa3ImportStorageManifest.ReceiptDocumentKind
            ],
            units.Select(unit => unit.Id.Value).Order(StringComparer.Ordinal));
        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(Elsa3ImportStorageManifest.StorageSchemaVersion, unit.SchemaVersion);
            Assert.Equal([Elsa3ImportStorageManifest.IdField], unit.Key.Columns);
            Assert.Equal(PortableType.String, unit.Columns.Single(column => column.Name == Elsa3ImportStorageManifest.IdField).Type);
            Assert.Equal(PortableType.Json, unit.Columns.Single(column => column.Name == Elsa3ImportStorageManifest.ContentField).Type);
            Assert.Equal(PortableType.Int64, unit.Columns.Single(column => column.Name == Elsa3ImportStorageManifest.RevisionField).Type);
            Assert.Contains(unit.Columns, column => column.Name == Elsa3ImportStorageManifest.SearchTextField);
        });
    }
}
