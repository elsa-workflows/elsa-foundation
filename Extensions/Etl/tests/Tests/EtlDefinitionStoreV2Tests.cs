using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed class EtlDefinitionStoreV2Tests
{
    private static readonly EtlScope Scope = new("tenant-a", "partition-a");

    [Fact]
    public async Task V2_draft_and_publication_survive_reopen_with_exact_identity_and_catalog_format()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var pipeline = EtlV2TestPipelines.Create();
        var artifact = Compile(1, pipeline);
        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var created = await store.CreateDraftAsync(Scope, "orders", pipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
            Assert.Equal(EtlPublicationStatus.Published, (await store.PublishAsync(Scope, "orders", created.Draft!.Revision, artifact)).Status);
        }
        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var draft = Assert.IsType<EtlDefinitionDraftV2>(await ((IEtlDefinitionStoreV2)store).FindDraftAsync(Scope, "orders"));
            var published = Assert.IsType<EtlArtifact>(await store.FindPublishedAsync(Scope, "orders", 1));
            Assert.Equal(1, draft.LastPublishedVersion);
            Assert.Equal(artifact.ArtifactId, published.ArtifactId);
            Assert.Equal(artifact.ContentHash, published.ContentHash);
            Assert.Equal(artifact.Pipeline.GetRawText(), published.Pipeline.GetRawText());
            Assert.Equal(artifact.Pipeline.GetRawText(), EtlArtifactV2Json.Write(draft.Pipeline).GetRawText());
            var catalog = new EfEtlDefinitionCatalog(context);
            Assert.Equal(2, Assert.Single(await catalog.ListDefinitionsAsync(Scope, null, 10)).FormatVersion);
            Assert.Equal(2, Assert.Single(await catalog.ListVersionsAsync(Scope, "orders", 0, 1, 10)).FormatVersion);
            Assert.False(context.Database.HasPendingModelChanges());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task V2_updates_are_scoped_and_revisioned_and_publication_replay_survives_a_later_edit()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var v2 = (IEtlDefinitionStoreV2)store;
        var original = EtlV2TestPipelines.Create();
        var changed = original with { Routes = new(new EtlJsonlDestination("other"), new EtlHttpDestination("errors")) };
        await v2.CreateDraftAsync(Scope, "orders", original);
        var first = Compile(1, original);
        Assert.Equal(EtlPublicationStatus.Published, (await store.PublishAsync(Scope, "orders", 1, first)).Status);
        Assert.Equal(EtlDefinitionWriteStatus.Updated, (await v2.UpdateDraftAsync(Scope, "orders", 1, changed)).Status);
        Assert.Equal(EtlDefinitionWriteStatus.Conflict, (await v2.UpdateDraftAsync(Scope, "orders", 1, original)).Status);
        Assert.Equal(EtlPublicationStatus.Replay, (await store.PublishAsync(Scope, "orders", 1, first)).Status);
        Assert.Equal(EtlPublicationStatus.Conflict, (await store.PublishAsync(Scope, "orders", 2, Compile(1, changed))).Status);
        Assert.Equal(EtlPublicationStatus.Published, (await store.PublishAsync(Scope, "orders", 2, Compile(2, changed))).Status);
        foreach (var other in new[] { new EtlScope("other-tenant", Scope.PersistencePartition), new EtlScope(Scope.TenantId, "other-partition") })
        {
            Assert.Null(await v2.FindDraftAsync(other, "orders"));
            Assert.Null(await store.FindPublishedAsync(other, "orders", 1));
            Assert.Equal(EtlDefinitionWriteStatus.NotFound, (await v2.UpdateDraftAsync(other, "orders", 1, changed)).Status);
        }
    }

    [Fact]
    public async Task Explicit_v1_to_v2_upgrade_preserves_old_publication_and_refuses_implicit_downgrade()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var v1 = EtlTestPipelines.Create();
        var old = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 1, v1).Artifact);
        await store.CreateDraftAsync(Scope, "orders", v1);
        await store.PublishAsync(Scope, "orders", 1, old);
        var upgraded = await store.UpdateDraftAsync(Scope, "orders", 1, EtlV2TestPipelines.Create());
        Assert.Equal(EtlDefinitionWriteStatus.Updated, upgraded.Status);
        Assert.Equal(2, upgraded.Draft!.Revision);
        Assert.Equal(1, upgraded.Draft.LastPublishedVersion);
        Assert.Equal(EtlPublicationStatus.Published, (await store.PublishAsync(Scope, "orders", 2, Compile(2, upgraded.Draft.Pipeline))).Status);
        Assert.Equal(EtlPublicationStatus.Replay, (await store.PublishAsync(Scope, "orders", 1, old)).Status);
        Assert.Equal(old.Pipeline.GetRawText(), (await store.FindPublishedAsync(Scope, "orders", 1))!.Pipeline.GetRawText());
        await Assert.ThrowsAsync<NotSupportedException>(async () => await store.FindDraftAsync(Scope, "orders"));
        var downgrade = await store.UpdateDraftAsync(Scope, "orders", 2, v1);
        Assert.Equal(EtlDefinitionWriteStatus.Conflict, downgrade.Status);
        Assert.Null(downgrade.Draft);
        Assert.Equal(EtlDefinitionWriteStatus.Conflict, (await store.CreateDraftAsync(Scope, "orders", v1)).Status);
        Assert.Equal(2, (await ((IEtlDefinitionStoreV2)store).FindDraftAsync(Scope, "orders"))!.Revision);
    }

    [Fact]
    public async Task Missing_v2_compiler_or_forged_artifact_cannot_publish_or_advance_version()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var pipeline = EtlV2TestPipelines.Create();
        await store.CreateDraftAsync(Scope, "orders", pipeline);
        var artifact = Compile(1, pipeline);
        var onlyV1Compiler = new EfEtlDefinitionStore(context, new EtlArtifactCompiler());
        Assert.Equal(EtlPublicationStatus.InvalidArtifact, (await onlyV1Compiler.PublishAsync(Scope, "orders", 1, artifact)).Status);
        var forged = new EtlArtifact(2, "wrong-id", artifact.ContentHash, "orders", 1, artifact.Pipeline);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact, (await store.PublishAsync(Scope, "orders", 1, forged)).Status);
        var unknown = new EtlArtifact(3, artifact.ArtifactId, artifact.ContentHash, "orders", 1, artifact.Pipeline);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact, (await store.PublishAsync(Scope, "orders", 1, unknown)).Status);
        var invalidPipeline = pipeline with { Http = pipeline.Http with { MaxAttempts = 0 } };
        var invalidJson = EtlArtifactV2Json.Write(invalidPipeline);
        var invalidHash = EtlArtifactJson.Hash(invalidJson);
        var invalid = new EtlArtifact(2, EtlArtifactJson.CreateArtifactId(2, "orders", 1, invalidHash), invalidHash, "orders", 1, invalidJson);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact, (await store.PublishAsync(Scope, "orders", 1, invalid)).Status);
        Assert.Empty(await context.PublishedArtifacts.ToListAsync());
        Assert.Equal(0, (await ((IEtlDefinitionStoreV2)store).FindDraftAsync(Scope, "orders"))!.LastPublishedVersion);
    }

    [Fact]
    public async Task Persisted_format_mismatch_and_unknown_format_are_not_silently_reinterpreted()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var pipeline = EtlV2TestPipelines.Create();
        await store.CreateDraftAsync(Scope, "orders", pipeline);
        await store.PublishAsync(Scope, "orders", 1, Compile(1, pipeline));
        await context.PublishedArtifacts.ExecuteUpdateAsync(x => x.SetProperty(r => r.FormatVersion, 1));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindPublishedAsync(Scope, "orders", 1));
        await context.Drafts.ExecuteUpdateAsync(x => x.SetProperty(r => r.FormatVersion, 99));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await ((IEtlDefinitionStoreV2)store).FindDraftAsync(Scope, "orders"));
    }

    [Theory]
    [InlineData('x')]
    [InlineData('界')]
    public async Task Oversized_v2_draft_is_rejected_before_json_materialization(char character)
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        await store.CreateDraftAsync(Scope, "orders", EtlV2TestPipelines.Create());
        // The multibyte case is under the character limit but over the UTF-8 limit.
        var content = new string(character, character == 'x' ? EtlArtifact.MaximumByteLength + 1 : EtlArtifact.MaximumByteLength / 2);
        await context.Drafts.ExecuteUpdateAsync(x => x.SetProperty(r => r.PipelineJson, content));
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await ((IEtlDefinitionStoreV2)store).FindDraftAsync(Scope, "orders"));
        Assert.Contains("byte limit", error.Message);
    }

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

    private static EfEtlDefinitionStore Store(EtlDesignDbContext context)
    {
        var compiler = new EtlArtifactCompiler();
        return new(context, compiler, compiler);
    }

    private static EtlArtifact Compile(int version, EtlPipelineV2 pipeline) =>
        Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", version, pipeline).Artifact);
}
