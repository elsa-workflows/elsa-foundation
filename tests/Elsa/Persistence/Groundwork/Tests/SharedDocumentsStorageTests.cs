using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Pins the frozen shared-documents recipe: what a projected index is allowed to look like, and the
/// physical shape the recipe derives from it. The recipe replicates the retired legacy bridge, so
/// these are schema-compatibility guarantees, not implementation preferences.
/// </summary>
public class SharedDocumentsStorageTests
{
    private static SharedDocumentsIndex Index(
        bool projected,
        MissingValueBehavior missingValueBehavior = MissingValueBehavior.Excluded,
        params string[] fields) => new(
        new LogicalIndexDeclaration(
            "by-subject",
            [.. fields.DefaultIfEmpty("/subject").Select(field => new IndexField(field))],
            IndexValueKind.Keyword,
            false,
            missingValueBehavior),
        projected);

    [Fact]
    public void Projected_index_produces_bounded_column_scope_prefixed_index_and_projection_table()
    {
        var storage = SharedDocumentsStorage.Create(
            "testDocument", TenancyPolicy.Scoped, [Index(projected: true)], []);

        var definition = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var column = Assert.Single(definition.ProjectedColumns);
        Assert.Equal("by-subject", column.LogicalName);
        Assert.Equal("/subject", column.Path);
        Assert.Equal(SharedDocumentsStorage.StringProjectionLength, column.Length);
        var index = Assert.Single(definition.Indexes);
        Assert.Equal(2, index.Columns.Count);
        Assert.Equal("storage_scope", index.Columns[0].ColumnLogicalName);
        Assert.Equal("by-subject", index.Columns[1].ColumnLogicalName);
        Assert.Equal("testDocument_projection", definition.LinkedProjectionLogicalName);
    }

    [Fact]
    public void Global_tenancy_omits_the_scope_prefix_column()
    {
        var storage = SharedDocumentsStorage.Create(
            "testDocument", TenancyPolicy.Global, [Index(projected: true)], []);

        var definition = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var index = Assert.Single(definition.Indexes);
        Assert.Equal("by-subject", Assert.Single(index.Columns).ColumnLogicalName);
    }

    [Fact]
    public void Unprojected_unit_declares_no_projection_table()
    {
        var storage = SharedDocumentsStorage.Create(
            "testDocument", TenancyPolicy.Scoped, [Index(projected: false)], []);

        var definition = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        Assert.Empty(definition.ProjectedColumns);
        Assert.Empty(definition.Indexes);
        Assert.Null(definition.LinkedProjectionLogicalName);
        Assert.Single(storage.LogicalIndexes);
    }

    [Fact]
    public void Projecting_a_compound_index_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SharedDocumentsStorage.Create(
            "testDocument", TenancyPolicy.Scoped, [Index(projected: true, fields: ["/a", "/b"])], []));
        Assert.Contains("single-field", exception.Message);
    }

    [Fact]
    public void Projecting_an_included_as_null_index_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SharedDocumentsStorage.Create(
            "testDocument", TenancyPolicy.Scoped,
            [Index(projected: true, MissingValueBehavior.IncludedAsNull)], []));
        Assert.Contains("MissingValueBehavior.Excluded", exception.Message);
    }
}
