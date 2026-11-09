using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Tests.Support;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlV1CompatibilityFixtureTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Retained_v1_published_artifact_and_native_workflow_keep_their_original_identity()
    {
        var fixture = EtlV1CompatibilityFixture.Open();
        fixture.VerifyFiles();

        var artifact = fixture.ReadArtifact();
        Assert.Equal(1, artifact.FormatVersion);
        Assert.Equal(fixture.Manifest.EtlArtifactId, artifact.ArtifactId);
        Assert.Equal(fixture.Manifest.EtlContentHash, artifact.ContentHash);
        Assert.Equal(fixture.Manifest.DefinitionId, artifact.DefinitionId);
        Assert.Equal(fixture.Manifest.DefinitionVersion, artifact.DefinitionVersion);
        Assert.Equal(EtlArtifactJson.Hash(artifact.Pipeline), artifact.ContentHash);
        var pipeline = EtlArtifactJson.Read(artifact.Pipeline);
        Assert.Equal(artifact.Pipeline.GetRawText(), EtlArtifactJson.Write(pipeline).GetRawText());
        Assert.Equal("source", pipeline.Bindings.Source);
        Assert.Equal("output", pipeline.Bindings.Output);

        using var executableDocument = JsonDocument.Parse(File.ReadAllBytes(fixture.FilePath("workflow-executable.json")));
        var root = executableDocument.RootElement;
        var identity = root.GetProperty("identity");
        Assert.Equal(fixture.Manifest.WorkflowArtifactId, identity.GetProperty("artifactId").GetString());
        Assert.Equal(fixture.Manifest.WorkflowArtifactHash, identity.GetProperty("artifactHash").GetString());
        Assert.Equal(fixture.Manifest.WorkflowDefinitionVersionId, identity.GetProperty("definitionVersionId").GetString());

        var runEtlNode = Assert.Single(root.GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("activityType").GetString() == RunEtl.ActivityType);
        var selection = EtlPublishedSelection.Parse(runEtlNode.GetProperty("inputBindings")
            .GetProperty(nameof(RunEtl.Pipeline)).GetProperty("literalValue").GetString()!);
        Assert.Equal(fixture.Manifest.DefinitionId, selection.DefinitionId);
        Assert.Equal(fixture.Manifest.DefinitionVersion, selection.DefinitionVersion);
        Assert.Equal(fixture.Manifest.EtlContentHash, selection.ContentHash);

        var embeddedArtifact = JsonSerializer.Deserialize<EtlArtifact>(
            runEtlNode.GetProperty("metadata").GetProperty("etl.pipeline.v1").GetString()!, WebJson);
        Assert.NotNull(embeddedArtifact);
        Assert.Equal(1, embeddedArtifact.FormatVersion);
        Assert.Equal(artifact.ArtifactId, embeddedArtifact.ArtifactId);
        Assert.Equal(artifact.ContentHash, embeddedArtifact.ContentHash);
        Assert.Equal(EtlArtifactJson.Write(pipeline).GetRawText(),
            EtlArtifactJson.Write(EtlArtifactJson.Read(embeddedArtifact.Pipeline)).GetRawText());
    }

    [Fact]
    public async Task Runtime_artifact_store_reads_the_retained_native_publication_without_design_services()
    {
        var fixture = EtlV1CompatibilityFixture.Open();
        fixture.VerifyFiles();
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.V1PublicationReplay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "workflow.db");
        try
        {
            File.Copy(fixture.FilePath("published/workflow.db"), database);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPersistenceCore("partition-test").AddRuntimeArtifactsEntityFrameworkCore(
                new RuntimeArtifactsEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={database};Pooling=False"
                });
            await using var provider = services.BuildServiceProvider();
            await using var operation = provider.CreateAsyncScope();
            Assert.Null(operation.ServiceProvider.GetService<IEtlArtifactCompiler>());
            Assert.Null(operation.ServiceProvider.GetService<IEtlDefinitionStore>());
            await operation.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync();
            var executable = await operation.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
                .FindAsync(fixture.Manifest.WorkflowArtifactId);
            Assert.NotNull(executable);
            Assert.Equal(fixture.Manifest.WorkflowArtifactHash, executable.Identity.ArtifactHash);
            Assert.Equal(fixture.Manifest.WorkflowDefinitionVersionId, executable.Identity.DefinitionVersionId);
            using var expected = JsonDocument.Parse(File.ReadAllBytes(fixture.FilePath("workflow-executable.json")));
            Assert.Equal(expected.RootElement.GetRawText(), JsonSerializer.SerializeToElement(executable, WebJson).GetRawText());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            fixture.VerifyFiles();
        }
    }

    [Fact]
    public async Task Runtime_store_migrates_and_accepts_the_retained_owed_effect_without_retargeting_binding_handles()
    {
        var fixture = EtlV1CompatibilityFixture.Open();
        fixture.VerifyFiles();
        var destination = Path.Combine(Path.GetTempPath(), "Elsa.Etl.V1FixtureReplay", Guid.NewGuid().ToString("N"));
        fixture.CopyRuntimeTo(destination);
        var replaySnapshotRoot = Path.Combine(destination, "snapshots");
        var replayOutputRoot = Path.Combine(destination, "output");

        var clock = new FixedTimeProvider(fixture.Manifest.CapturedLeaseExpiresAt);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        new EtlRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = $"Data Source={Path.Combine(destination, "runtime.db")};Pooling=False"
        }.ConfigureServices(services);

        try
        {
            await using var provider = services.BuildServiceProvider();
            await using var operation = provider.CreateAsyncScope();
            Assert.Null(operation.ServiceProvider.GetService<IEtlArtifactCompiler>());
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            await db.Database.MigrateAsync();

            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            var scope = new EtlScope(fixture.Manifest.TenantId, fixture.Manifest.PersistencePartition);
            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, fixture.Manifest.RuntimeRunId));
            Assert.Equal(EtlRunStatus.Running, run.Status);
            Assert.Equal(fixture.Manifest.Bindings, run.Bindings);
            Assert.Equal(fixture.Manifest.Prepared, run.Prepared);
            var bindings = Assert.IsType<EtlRunBindings>(run.Bindings);
            var prepared = Assert.IsType<EtlPreparedRun>(run.Prepared);
            Assert.Equal(fixture.Manifest.EtlContentHash, run.Invocation.Artifact.ContentHash);
            Assert.Equal(fixture.Manifest.AcceptedRecordsBeforeAcceptance, run.AcceptedRecords);
            Assert.Equal(fixture.Manifest.OutstandingRecordsBeforeAcceptance, run.OutstandingRecords);
            Assert.Equal(fixture.Manifest.OutstandingBytesBeforeAcceptance, run.OutstandingBytes);

            var originalPartition = Assert.Single(await store.ListPartitionsAsync(scope, run.Invocation.RunId, -1, 10),
                item => item.PartitionOrdinal == fixture.Manifest.PartitionOrdinal);
            Assert.Equal(fixture.Manifest.CapturedFence, originalPartition.Fence);
            Assert.Equal(fixture.Manifest.ReservedBatch, Assert.IsType<EtlBatchReservation>(originalPartition.OwedBatch).Batch);

            var pipeline = EtlArtifactJson.Read(run.Invocation.Artifact.Pipeline);
            var sourcePin = EtlRuntimeMetadata.PinnedBinding(scope, bindings.Source, EtlFileRole.Source);
            var lookupPin = EtlRuntimeMetadata.PinnedBinding(scope, bindings.Lookup, EtlFileRole.Lookup);
            var snapshots = new EtlInputSnapshots(scope, run.Invocation.RunId, sourcePin, lookupPin,
                ToChunk(prepared.Source), ToChunk(prepared.Lookup));
            var preparer = new EtlFilePreparer(new HostFileBindings([]), replaySnapshotRoot);
            var rehydrated = await preparer.PrepareAsync(pipeline, snapshots);
            Assert.True(rehydrated.Succeeded, rehydrated.Diagnostic?.Code);
            Assert.Equal(prepared, EtlRuntimeMetadata.Prepared(rehydrated.Prepared!));

            await using (var builder = await EtlInputBatchBuilder.OpenAsync(replaySnapshotRoot, pipeline, rehydrated.Prepared!))
            {
                var rebuilt = await builder.RebuildAsync(fixture.Manifest.PartitionOrdinal,
                    fixture.Manifest.ReservedBatch.StartOrdinal, fixture.Manifest.ReservedBatch.EndOrdinalExclusive,
                    fixture.Manifest.ReservedBatch.InputBytes, fixture.Manifest.ReservedBatch.InputHash);
                Assert.True(rebuilt.Succeeded, rebuilt.Diagnostic?.Code);
                Assert.NotNull(rebuilt.Batch);
                var admission = new EtlRunAdmission(run.Invocation, run.Bindings);
                var rebuiltDescriptor = EtlRuntimeMetadata.Batch(admission, rehydrated.Prepared!, rebuilt.Batch!);
                Assert.Equal(fixture.Manifest.ReservedBatch, rebuiltDescriptor);
            }

            var acceptance = fixture.ReadAcceptance();
            Assert.Equal(fixture.Manifest.ReservedBatch, acceptance.Batch);
            Assert.Equal(fixture.Manifest.MainEffect, Assert.Single(acceptance.Effects));
            Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(
                Path.Combine(replayOutputRoot, fixture.Manifest.MainEffect.Chunk.Key)));
            var outputBytes = await File.ReadAllBytesAsync(Path.Combine(replayOutputRoot, fixture.Manifest.MainEffect.Chunk.Key));
            Assert.Equal(fixture.Manifest.MainEffect.Chunk.ByteLength, outputBytes.LongLength);
            Assert.Equal(fixture.Manifest.MainEffect.Chunk.ContentHash,
                Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant());

            var successorLease = await store.ClaimPartitionAsync(scope, run.Invocation.RunId, "v1-fixture-successor");
            Assert.NotNull(successorLease);
            Assert.True(successorLease.Fence > fixture.Manifest.CapturedFence);
            Assert.Equal(fixture.Manifest.PartitionOrdinal, successorLease.PartitionOrdinal);
            Assert.Equal(fixture.Manifest.ReservedBatch.StartOrdinal, successorLease.NextOrdinal);

            var accepted = await store.AcceptBatchAsync(successorLease, acceptance);
            Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
            var completedPartition = Assert.Single(await store.ListPartitionsAsync(scope, run.Invocation.RunId, -1, 10),
                item => item.PartitionOrdinal == fixture.Manifest.PartitionOrdinal);
            Assert.Equal(fixture.Manifest.ReservedBatch.EndOrdinalExclusive, completedPartition.NextOrdinal);
            Assert.Null(completedPartition.OwedBatch);

            var after = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, run.Invocation.RunId));
            Assert.Equal(fixture.Manifest.Bindings, after.Bindings);
            Assert.Equal(fixture.Manifest.Prepared, after.Prepared);
            Assert.Equal(1, after.AcceptedRecords);
            Assert.Equal(0, after.OutstandingRecords);
            Assert.Equal(0, after.OutstandingBytes);
            Assert.Equal(EtlRunStatus.Running, after.Status);
            var manifest = Assert.Single(await store.ListManifestAsync(scope, run.Invocation.RunId, null, 10));
            Assert.Equal(fixture.Manifest.ReservedBatch.BatchId, manifest.BatchId);
            Assert.Equal(fixture.Manifest.MainEffect, manifest.Effect);
        }
        finally
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            fixture.VerifyFiles();
        }
    }

    private static EtlChunk ToChunk(EtlObjectReference reference) =>
        new(reference.Key, reference.ContentHash, reference.ByteLength);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
