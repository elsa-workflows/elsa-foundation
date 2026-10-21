using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed class EtlDefinitionStoreTests
{
    [Fact]
    public async Task Fresh_schema_persists_draft_and_published_artifact_after_reopen()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var artifact = Compile("orders", 1, pipeline);

        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var created = await store.CreateDraftAsync(scope, "orders", pipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
            Assert.Equal(1, created.Draft!.Revision);
            Assert.Equal(EtlPublicationStatus.Published,
                (await store.PublishAsync(scope, "orders", created.Draft.Revision, artifact)).Status);
        }

        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var draft = Assert.IsType<EtlDefinitionDraft>(await store.FindDraftAsync(scope, "orders"));
            var published = Assert.IsType<EtlArtifact>(await store.FindPublishedAsync(scope, "orders", 1));

            Assert.Equal(1, draft.LastPublishedVersion);
            Assert.Equal(EtlArtifactJson.Write(pipeline).GetRawText(), EtlArtifactJson.Write(draft.Pipeline).GetRawText());
            Assert.Equal(artifact.ArtifactId, published.ArtifactId);
            Assert.Equal(EtlArtifactJson.Write(pipeline).GetRawText(), published.Pipeline.GetRawText());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        }
    }

    [Fact]
    public async Task Draft_updates_and_publication_use_optimistic_revision_checks()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var initial = EtlTestPipelines.Create();
        var changed = EtlTestPipelines.Create("renamed");
        var created = await store.CreateDraftAsync(scope, "orders", initial);

        var updated = await store.UpdateDraftAsync(scope, "orders", created.Draft!.Revision, changed);
        Assert.Equal(EtlDefinitionWriteStatus.Updated, updated.Status);
        Assert.Equal(2, updated.Draft!.Revision);

        var staleUpdate = await store.UpdateDraftAsync(scope, "orders", expectedRevision: 1, initial);
        Assert.Equal(EtlDefinitionWriteStatus.Conflict, staleUpdate.Status);
        Assert.Equal(2, staleUpdate.Draft!.Revision);

        var stalePublication = await store.PublishAsync(scope, "orders", expectedRevision: 1, Compile("orders", 1, initial));
        Assert.Equal(EtlPublicationStatus.Conflict, stalePublication.Status);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
    }

    [Fact]
    public async Task Draft_and_publication_reads_are_isolated_by_tenant_and_partition()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var owner = new EtlScope("tenant-a", "partition-a");
        var otherTenant = new EtlScope("tenant-b", "partition-a");
        var otherPartition = new EtlScope("tenant-a", "partition-b");
        var pipeline = EtlTestPipelines.Create();

        var created = await store.CreateDraftAsync(owner, "orders", pipeline);
        Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
        Assert.Null(await store.FindDraftAsync(otherTenant, "orders"));
        Assert.Null(await store.FindDraftAsync(otherPartition, "orders"));
        var tenantPipeline = EtlTestPipelines.Create("tenant-output");
        var partitionPipeline = EtlTestPipelines.Create("partition-output");
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(otherTenant, "orders", tenantPipeline)).Status);
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(otherPartition, "orders", partitionPipeline)).Status);
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(owner, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline))).Status);

        Assert.Equal(EtlArtifactJson.Write(tenantPipeline).GetRawText(),
            EtlArtifactJson.Write((await store.FindDraftAsync(otherTenant, "orders"))!.Pipeline).GetRawText());
        Assert.Equal(EtlArtifactJson.Write(partitionPipeline).GetRawText(),
            EtlArtifactJson.Write((await store.FindDraftAsync(otherPartition, "orders"))!.Pipeline).GetRawText());
        Assert.Null(await store.FindPublishedAsync(otherTenant, "orders", 1));
        Assert.Null(await store.FindPublishedAsync(otherPartition, "orders", 1));
    }

    [Fact]
    public async Task Published_artifacts_remain_immutable_and_exact_retries_converge()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var originalPipeline = EtlTestPipelines.Create();
        var originalArtifact = Compile("orders", 1, originalPipeline);
        var created = await store.CreateDraftAsync(scope, "orders", originalPipeline);

        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, originalArtifact)).Status);
        Assert.Equal(EtlPublicationStatus.Replay,
            (await store.PublishAsync(scope, "orders", created.Draft.Revision, originalArtifact)).Status);

        var revisedPipeline = EtlTestPipelines.Create("renamed");
        var updated = await store.UpdateDraftAsync(scope, "orders", created.Draft.Revision, revisedPipeline);
        Assert.Equal(EtlDefinitionWriteStatus.Updated, updated.Status);

        // A retry of a committed publication remains idempotent after a later draft edit.
        Assert.Equal(EtlPublicationStatus.Replay,
            (await store.PublishAsync(scope, "orders", created.Draft.Revision, originalArtifact)).Status);
        Assert.Equal(EtlPublicationStatus.Conflict,
            (await store.PublishAsync(scope, "orders", updated.Draft!.Revision, Compile("orders", 1, revisedPipeline))).Status);

        var secondVersion = Compile("orders", 2, revisedPipeline);
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", updated.Draft.Revision, secondVersion)).Status);
        Assert.Equal(originalArtifact.ArtifactId, (await store.FindPublishedAsync(scope, "orders", 1))!.ArtifactId);
        Assert.Equal(secondVersion.ArtifactId, (await store.FindPublishedAsync(scope, "orders", 2))!.ArtifactId);
    }

    [Fact]
    public async Task Publish_recompiles_and_rejects_invalid_or_forged_artifacts()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        var valid = Compile("orders", 1, pipeline);

        var invalidPipeline = pipeline with { Limits = new EtlLimits { PartitionCount = 0 } };
        var invalidDocument = EtlArtifactJson.Write(invalidPipeline);
        var wellHashedInvalid = new EtlArtifact(
            EtlArtifact.CurrentFormatVersion,
            "forged-id",
            EtlArtifactJson.Hash(invalidDocument),
            "orders",
            1,
            invalidDocument);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, wellHashedInvalid)).Status);

        var forgedId = new EtlArtifact(
            valid.FormatVersion,
            "forged-id",
            valid.ContentHash,
            valid.DefinitionId,
            valid.DefinitionVersion,
            valid.Pipeline);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact,
            (await store.PublishAsync(scope, "orders", created.Draft.Revision, forgedId)).Status);

        var wrongDefinition = new EtlArtifact(
            valid.FormatVersion,
            valid.ArtifactId,
            valid.ContentHash,
            "another-definition",
            valid.DefinitionVersion,
            valid.Pipeline);
        Assert.Equal(EtlPublicationStatus.InvalidArtifact,
            (await store.PublishAsync(scope, "orders", created.Draft.Revision, wrongDefinition)).Status);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
    }

    [Fact]
    public async Task Revision_and_publication_version_ceiling_conflict_without_overflow()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);

        Assert.Equal(1, await context.Drafts
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, long.MaxValue)));
        var revisionConflict = await store.UpdateDraftAsync(scope, "orders", long.MaxValue, EtlTestPipelines.Create("renamed"));
        Assert.Equal(EtlDefinitionWriteStatus.Conflict, revisionConflict.Status);
        Assert.Equal(long.MaxValue, revisionConflict.Draft!.Revision);

        Assert.Equal(1, await context.Drafts
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastPublishedVersion, int.MaxValue)));
        var versionConflict = await store.PublishAsync(scope, "orders", long.MaxValue, Compile("orders", 1, pipeline));
        Assert.Equal(EtlPublicationStatus.Conflict, versionConflict.Status);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
    }

    private static EfEtlDefinitionStore Store(EtlDesignDbContext context) => new(context, new EtlArtifactCompiler());

    [Fact]
    public async Task Unrelated_publication_storage_failure_remains_visible_and_rolls_back_the_version()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        var artifact = Compile("orders", 1, pipeline);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER refuse_fixture_publication BEFORE INSERT ON elsa_etl_published_artifacts
            BEGIN SELECT RAISE(ABORT, 'fixture storage refusal'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact));
        Assert.Equal(0, (await store.FindDraftAsync(scope, "orders"))!.LastPublishedVersion);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_fixture_publication");
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact)).Status);
    }

    [Fact]
    public async Task Oversized_forged_artifact_is_refused_without_publishing_or_advancing_the_draft()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        var valid = Compile("orders", 1, pipeline);
        var large = EtlArtifactJson.Write(EtlTestPipelines.Create(new string('x', EtlArtifact.MaximumByteLength)));
        var forged = new EtlArtifact(valid.FormatVersion, valid.ArtifactId, EtlArtifactJson.Hash(large), "orders", 1, large);

        Assert.Equal(EtlPublicationStatus.InvalidArtifact,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, forged)).Status);
        Assert.Equal(0, (await store.FindDraftAsync(scope, "orders"))!.LastPublishedVersion);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
    }

    private static EtlArtifact Compile(string definitionId, int version, EtlPipeline pipeline) =>
        Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile(definitionId, version, pipeline).Artifact);
}
