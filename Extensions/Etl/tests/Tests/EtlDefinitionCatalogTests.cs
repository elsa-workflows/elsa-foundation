using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed class EtlDefinitionCatalogTests
{
    [Fact]
    public async Task Definition_pages_use_stable_hash_cursors_and_preserve_opaque_ids()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var scope = new EtlScope("tenant-a", "partition-a");
        var store = Store(context);
        var catalog = Catalog(context);
        var ids = new[] { "orders", "opaque id/with spaces", "customers", "Orders" };
        foreach (var id in ids)
            Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(scope, id, EtlTestPipelines.Create())).Status);

        var orderedIds = ids.OrderBy(id => EtlDefinitionRecordCodec.Definition(id).Key, StringComparer.Ordinal).ToArray();
        var firstPage = await catalog.ListDefinitionsAsync(scope, afterDefinitionId: null, take: 2);
        Assert.Equal(orderedIds.Take(2), firstPage.Select(row => row.DefinitionId));

        var secondPage = await catalog.ListDefinitionsAsync(scope, firstPage[^1].DefinitionId, take: 2);
        Assert.Equal(orderedIds.Skip(2), secondPage.Select(row => row.DefinitionId));
        Assert.Equal(4, (await catalog.ListDefinitionsAsync(scope, null, take: 1000)).Count);
        Assert.Contains(firstPage.Concat(secondPage), row => row.DefinitionId == "opaque id/with spaces");

        // The cursor is a key derived from its ID, not a lookup that requires the ID to remain present.
        const string missingCursor = "never-created-cursor";
        var cursorPage = await catalog.ListDefinitionsAsync(scope, missingCursor, take: 10);
        var cursorKey = EtlDefinitionRecordCodec.Definition(missingCursor).Key;
        Assert.Equal(orderedIds.Where(id => StringComparer.Ordinal.Compare(EtlDefinitionRecordCodec.Definition(id).Key, cursorKey) > 0),
            cursorPage.Select(row => row.DefinitionId));
    }

    [Fact]
    public async Task Definition_pages_keep_tenant_partition_and_definition_identity_case_sensitive()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var catalog = Catalog(context);
        var owner = new EtlScope("Tenant-A", "Partition-A");
        var otherTenant = new EtlScope("tenant-A", "Partition-A");
        var otherPartition = new EtlScope("Tenant-A", "partition-A");

        foreach (var id in new[] { "Orders", "orders" })
            Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(owner, id, EtlTestPipelines.Create())).Status);
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(otherTenant, "Orders", EtlTestPipelines.Create())).Status);
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(otherPartition, "Orders", EtlTestPipelines.Create())).Status);

        Assert.Equal(new[] { "Orders", "orders" },
            (await catalog.ListDefinitionsAsync(owner, null, 10)).Select(row => row.DefinitionId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(new[] { "Orders" }, (await catalog.ListDefinitionsAsync(otherTenant, null, 10)).Select(row => row.DefinitionId));
        Assert.Equal(new[] { "Orders" }, (await catalog.ListDefinitionsAsync(otherPartition, null, 10)).Select(row => row.DefinitionId));
    }

    [Fact]
    public async Task Definition_and_version_page_sizes_are_hard_bounded()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var catalog = Catalog(context);
        var scope = new EtlScope("tenant-a", "partition-a");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await catalog.ListDefinitionsAsync(scope, null, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await catalog.ListDefinitionsAsync(scope, null, 1001));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await catalog.ListVersionsAsync(scope, "orders", 0, 1, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await catalog.ListVersionsAsync(scope, "orders", 0, 1, 1001));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await catalog.ListVersionsAsync(scope, "orders", 2, 1, 10));
    }

    [Fact]
    public async Task Version_pages_are_ascending_after_exclusive_and_keep_the_supplied_ceiling()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var scope = new EtlScope("tenant-a", "partition-a");
        var store = Store(context);
        var catalog = Catalog(context);
        var firstPipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", firstPipeline);
        var first = Compile("orders", 1, firstPipeline);
        Assert.Equal(EtlPublicationStatus.Published, (await store.PublishAsync(scope, "orders", created.Draft!.Revision, first)).Status);

        const int frozenCeiling = 1;
        var firstPage = await catalog.ListVersionsAsync(scope, "orders", afterVersion: 0, throughVersion: frozenCeiling, take: 1);
        Assert.Collection(firstPage, version =>
        {
            Assert.Equal("orders", version.DefinitionId);
            Assert.Equal(1, version.DefinitionVersion);
            Assert.Equal(first.ContentHash, version.ContentHash);
        });

        var secondPipeline = EtlTestPipelines.Create("renamed");
        var updated = await store.UpdateDraftAsync(scope, "orders", created.Draft.Revision, secondPipeline);
        var second = Compile("orders", 2, secondPipeline);
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", updated.Draft!.Revision, second)).Status);

        var definition = Assert.Single(await catalog.ListDefinitionsAsync(scope, null, take: 10));
        Assert.Equal(updated.Draft.Revision, definition.Revision);
        Assert.Equal(2, definition.LastPublishedVersion);

        var frozenRemainder = await catalog.ListVersionsAsync(scope, "orders", afterVersion: 1, throughVersion: frozenCeiling, take: 10);
        Assert.Empty(frozenRemainder);
        Assert.Empty(await catalog.ListVersionsAsync(scope, "orders", afterVersion: 0, throughVersion: 0, take: 10));

        var laterPage = await catalog.ListVersionsAsync(scope, "orders", afterVersion: 1, throughVersion: 2, take: 1);
        Assert.Collection(laterPage, version =>
        {
            Assert.Equal(2, version.DefinitionVersion);
            Assert.Equal(second.ContentHash, version.ContentHash);
        });
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("scope")]
    [InlineData("definition-key")]
    [InlineData("revision")]
    [InlineData("timestamp")]
    public async Task Definition_listing_rejects_corrupt_metadata(string corruption)
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var scope = new EtlScope("tenant-a", "partition-a");
        Assert.Equal(EtlDefinitionWriteStatus.Created,
            (await Store(context).CreateDraftAsync(scope, "orders", EtlTestPipelines.Create())).Status);

        var rows = context.Drafts.Where(row => row.ScopeKey == EtlDefinitionRecordCodec.Scope(scope).Key);
        switch (corruption)
        {
            case "schema":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SchemaVersion, "99.0.0"));
                await Assert.ThrowsAsync<EfSchemaVersionSkewException>(async () => await Catalog(context).ListDefinitionsAsync(scope, null, 10));
                break;
            case "scope":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PersistencePartition, EfRelationalIdentity.Encode("other-partition")));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Catalog(context).ListDefinitionsAsync(scope, null, 10));
                break;
            case "definition-key":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DefinitionKey, new string('A', 64)));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Catalog(context).ListDefinitionsAsync(scope, null, 10));
                break;
            case "revision":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 0L));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Catalog(context).ListDefinitionsAsync(scope, null, 10));
                break;
            case "timestamp":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UpdatedAtUtcTicks, -1L));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Catalog(context).ListDefinitionsAsync(scope, null, 10));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("definition-key")]
    [InlineData("content-hash")]
    [InlineData("envelope-hash")]
    [InlineData("timestamp")]
    public async Task Version_listing_rejects_corrupt_metadata(string corruption)
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await Store(context).CreateDraftAsync(scope, "orders", pipeline);
        Assert.Equal(EtlPublicationStatus.Published,
            (await Store(context).PublishAsync(scope, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline))).Status);

        var rows = context.PublishedArtifacts.Where(row => row.ScopeKey == EtlDefinitionRecordCodec.Scope(scope).Key);
        switch (corruption)
        {
            case "scope":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PersistencePartition, EfRelationalIdentity.Encode("other-partition")));
                break;
            case "definition-key":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DefinitionKey, new string('A', 64)));
                // The exact-key query no longer selects this row. Catalog validation covers selected
                // metadata, not a global consistency scan over unrelated or displaced rows.
                Assert.Empty(await Catalog(context).ListVersionsAsync(scope, "orders", 0, 1, 10));
                return;
            case "content-hash":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PipelineHash, new string('Z', 64)));
                break;
            case "envelope-hash":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ArtifactEnvelopeHash, new string('Z', 64)));
                break;
            case "timestamp":
                await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedAtUtcTicks, -1L));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () => await Catalog(context).ListVersionsAsync(scope, "orders", 0, 1, 10));
    }

    private static EfEtlDefinitionStore Store(EtlDesignDbContext context) => new(context, new EtlArtifactCompiler());
    private static EfEtlDefinitionCatalog Catalog(EtlDesignDbContext context) => new(context);
    private static EtlArtifact Compile(string definitionId, int version, EtlPipeline pipeline) =>
        Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile(definitionId, version, pipeline).Artifact);
}
