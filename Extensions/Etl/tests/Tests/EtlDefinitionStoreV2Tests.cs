using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>The design-store contract on SQLite, with the cases only SQLite can stage.</summary>
public sealed class EtlDefinitionStoreV2Tests : EtlDefinitionStoreV2Contract
{
    protected override async Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => await EtlDesignTestDatabase.CreateAsync();

    [Fact]
    public async Task Actual_pre_v2_design_database_migrates_without_rewriting_original_artifact_bytes()
    {
        var fixture = EtlV1CompatibilityFixture.Open();
        fixture.VerifyFiles();
        await using var database = await EtlDesignTestDatabase.CreateAsync(fixture.FilePath("published/etl-design.db"));
        await using var context = database.CreateContext();
        // The original publication producer used partition-test; runtime admission used runtime-journey.
        var scope = new EtlScope(null, "partition-test");
        var store = Store(context);
        var actual = Assert.IsType<EtlArtifact>(await store.FindPublishedAsync(scope, fixture.Manifest.DefinitionId, fixture.Manifest.DefinitionVersion));
        var original = fixture.ReadArtifact();
        Assert.Equal(original.ArtifactId, actual.ArtifactId);
        Assert.Equal(original.ContentHash, actual.ContentHash);
        Assert.Equal(original.Pipeline.GetRawText(), actual.Pipeline.GetRawText());
        Assert.All(await context.PublishedArtifacts.ToListAsync(), row => Assert.Equal(1, row.FormatVersion));
        var row = await context.PublishedArtifacts.SingleAsync(row => row.DefinitionVersion == fixture.Manifest.DefinitionVersion);
        Assert.Equal(JsonSerializer.Serialize(original, new JsonSerializerOptions(JsonSerializerDefaults.Web)), row.ArtifactJson);
        Assert.Equal(1, (await context.Drafts.SingleAsync()).FormatVersion);
        var catalog = new EfEtlDefinitionCatalog(context);
        Assert.Equal(1, Assert.Single(await catalog.ListDefinitionsAsync(scope, null, 10)).FormatVersion);
        var versions = await catalog.ListVersionsAsync(scope, fixture.Manifest.DefinitionId, 0, 2, 10);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, version => Assert.Equal(1, version.FormatVersion));
        Assert.False(context.Database.HasPendingModelChanges());
        fixture.VerifyFiles();
    }
}
