#if ETL_V1_FIXTURE_CAPTURE
using System.Text.Json;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    private const string V1ProducerBaseline = "b555bd8460270476e49496e519c69b65180e9505";
    private const string StableCatalogSourceId = "fixture://elsa-etl/published-workflow/v1";
    private static readonly JsonSerializerOptions V1FixtureJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CaptureAuthenticPreChangeV1Fixture()
    {
        var destination = Environment.GetEnvironmentVariable("ETL_V1_FIXTURE_CAPTURE_ROOT");
        var captureBaseCommit = Environment.GetEnvironmentVariable("ETL_V1_FIXTURE_CAPTURE_BASE_COMMIT");
        Assert.False(string.IsNullOrWhiteSpace(destination));
        Assert.False(string.IsNullOrWhiteSpace(captureBaseCommit));
        Assert.False(Directory.Exists(destination));
        Directory.CreateDirectory(destination!);

        await using var published = await EtlPublishedWorkflowFixture.CreateAsync(
            flowchart: false, catalogSourceId: StableCatalogSourceId);
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n");
        var pipeline = EtlArtifactJson.Read(published.OriginalArtifact.Pipeline);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation) = await files.PrepareWorkerRunAsync(pipeline, "v1-compatibility-run", deadline.Token);
        Assert.Equal(published.OriginalArtifact.ArtifactId, invocation.Artifact.ArtifactId);
        Assert.Equal(published.OriginalArtifact.ContentHash, invocation.Artifact.ContentHash);

        var worker = await files.StartWorkerAsync("v1-compatibility-capture", deadline.Token,
            storeGate: EtlWorkerStoreGate.BeforeAcceptance);
        var receipt = await worker.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(1, receipt.Acceptance.AcceptedRecords);
        await using (var operation = host.Services.CreateAsyncScope())
            Assert.Empty(await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
                .ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));

        EtlRunView retainedRun;
        EtlPartitionView retainedPartition;
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            retainedRun = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            retainedPartition = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10, deadline.Token),
                item => item.PartitionOrdinal == receipt.Lease.PartitionOrdinal);
        }

        Assert.Equal(EtlRunStatus.Running, retainedRun.Status);
        Assert.NotNull(retainedRun.Prepared);
        Assert.Equal(0, retainedRun.AcceptedRecords);
        Assert.Equal(receipt.Acceptance.Batch.EndOrdinalExclusive - receipt.Acceptance.Batch.StartOrdinal,
            retainedRun.OutstandingRecords);
        Assert.Equal(receipt.Lease.Owner, retainedPartition.Owner);
        Assert.Equal(receipt.Lease.Fence, retainedPartition.Fence);
        Assert.Equal(receipt.Acceptance.Batch, Assert.IsType<EtlBatchReservation>(retainedPartition.OwedBatch).Batch);
        var liveBindings = Assert.IsType<EtlRunBindings>(retainedRun.Bindings);
        var livePrepared = Assert.IsType<EtlPreparedRun>(retainedRun.Prepared);

        var effect = Assert.Single(receipt.Acceptance.Effects);
        Assert.Equal(EtlOutputRoute.Main, effect.Route);
        var outputPath = Path.Combine(files.OutputRoot, effect.Chunk.Key);
        Assert.True(File.Exists(outputPath));

        await worker.CrashAsync();
        await files.StopHostAsync(host);

        var runtimePath = Directory.GetParent(files.SnapshotRoot)!.FullName;
        var durableServices = new ServiceCollection();
        new EtlRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = $"Data Source={Path.Combine(runtimePath, "runtime.db")};Pooling=False"
        }.ConfigureServices(durableServices);
        await using (var durableProvider = durableServices.BuildServiceProvider())
        await using (var operation = durableProvider.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            retainedRun = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            retainedPartition = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10, deadline.Token),
                item => item.PartitionOrdinal == receipt.Lease.PartitionOrdinal);
        }

        Assert.Equal(liveBindings, retainedRun.Bindings);
        Assert.Equal(livePrepared, retainedRun.Prepared);
        Assert.Equal(receipt.Lease.Fence, retainedPartition.Fence);
        Assert.Equal(receipt.Acceptance.Batch, Assert.IsType<EtlBatchReservation>(retainedPartition.OwedBatch).Batch);
        var bindings = Assert.IsType<EtlRunBindings>(retainedRun.Bindings);
        var prepared = Assert.IsType<EtlPreparedRun>(retainedRun.Prepared);
        var capturedLeaseExpiresAt = Assert.IsType<DateTimeOffset>(retainedPartition.ExpiresAt);

        var publishedDirectory = Path.Combine(destination!, "published");
        var runtimeDirectory = Path.Combine(destination!, "runtime");
        Directory.CreateDirectory(publishedDirectory);
        Directory.CreateDirectory(runtimeDirectory);
        File.Copy(published.WorkflowDatabase, Path.Combine(publishedDirectory, "workflow.db"));
        File.Copy(published.EtlDesignDatabase, Path.Combine(publishedDirectory, "etl-design.db"));
        File.WriteAllBytes(Path.Combine(destination!, "etl-artifact.json"),
            JsonSerializer.SerializeToUtf8Bytes(published.OriginalArtifact, V1FixtureJson));
        File.WriteAllBytes(Path.Combine(destination!, "workflow-executable.json"),
            JsonSerializer.SerializeToUtf8Bytes(published.Executable, V1FixtureJson));

        File.Copy(Path.Combine(Directory.GetParent(files.SnapshotRoot)!.FullName, "runtime.db"),
            Path.Combine(runtimeDirectory, "runtime.db"));
        EtlV1CompatibilityFixture.CopyDirectory(files.SnapshotRoot, Path.Combine(runtimeDirectory, "snapshots"));
        EtlV1CompatibilityFixture.CopyDirectory(files.OutputRoot, Path.Combine(runtimeDirectory, "output"));
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "acceptance.json"),
            JsonSerializer.SerializeToUtf8Bytes(receipt.Acceptance, V1FixtureJson));

        var publicationPath = published.RootDirectory;
        AssertNoHostPaths(destination!, runtimePath, publicationPath);

        var manifest = new EtlV1CompatibilityManifest(
            1,
            V1ProducerBaseline,
            captureBaseCommit!,
            "uncommitted working-tree harness; CaptureBaseCommit does not contain these harness files",
            EtlV1CompatibilityFixture.DescribeFiles(
                EtlV1CompatibilityFixture.FindRepositoryRoot(),
                [
                    "Extensions/Etl/tests/Shared/EtlV1CompatibilityFixture.cs",
                    "Extensions/Etl/tests/Shared/capture-v1-compat-fixtures.sh",
                    "Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj",
                    "Extensions/Etl/tests/Tests/EtlV1CompatibilityFixtureCaptureTests.cs",
                    "Extensions/Etl/tests/Tests/EtlPublishedWorkflowFixture.cs",
                    "Extensions/Etl/tests/Tests/NativeEtlAuthoredComposite.cs"
                ]),
            published.OriginalArtifact.DefinitionId,
            published.OriginalArtifact.DefinitionVersion,
            published.OriginalArtifact.ArtifactId,
            published.OriginalArtifact.ContentHash,
            published.Executable.Identity.ArtifactId,
            published.Executable.Identity.ArtifactHash,
            published.Executable.Identity.DefinitionVersionId,
            files.Scope.TenantId,
            files.Scope.PersistencePartition,
            invocation.RunId,
            retainedPartition.PartitionOrdinal,
            retainedPartition.Fence,
            capturedLeaseExpiresAt,
            bindings,
            prepared,
            receipt.Acceptance.Batch,
            effect,
            retainedRun.AcceptedRecords,
            retainedRun.OutstandingRecords,
            retainedRun.OutstandingBytes,
            EtlV1CompatibilityFixture.DescribeFiles(destination!));
        File.WriteAllBytes(Path.Combine(destination!, "manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(manifest, V1FixtureJson));
        AssertNoHostPaths(destination!, runtimePath, publicationPath);
    }

    private static void AssertNoHostPaths(string root, string runtimePath, string publicationPath)
    {
        var forbiddenPaths = new[] { "/Users/", "/private/var/", "/tmp/", runtimePath, publicationPath }
            .Select(Encoding.UTF8.GetBytes).Distinct().ToArray();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            foreach (var forbidden in forbiddenPaths)
                Assert.True(bytes.AsSpan().IndexOf(forbidden) < 0, $"Retained compatibility payload contains a host path: {Path.GetFileName(file)}");
        }
    }
}
#endif
