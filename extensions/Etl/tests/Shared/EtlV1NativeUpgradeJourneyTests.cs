#if ETL_V1_NATIVE_UPGRADE
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

internal sealed record EtlV1NativeUpgradeHandoff(
    string ExecutionId,
    EtlScope Scope,
    IReadOnlyList<EtlHostFileBindingOptions> Bindings,
    EtlArtifact OriginalArtifact,
    EtlArtifact UpdatedArtifact,
    WorkflowExecutable Executable,
    WorkflowExecutableSourceReference Reference,
    EtlInvocation Invocation,
    EtlRunBindings RunBindings,
    EtlPreparedRun Prepared,
    EtlPartitionLease OldLease,
    EtlBatchAcceptance OldAcceptance,
    string PhysicalOutputSha256);

internal sealed record EtlV1NativeUpgradeSnapshot(
    EtlRunView Run,
    IReadOnlyList<EtlPartitionView> Partitions,
    IReadOnlyList<EtlManifestEntry> Manifest);

internal sealed partial class EtlPublishedWorkflowFixture
{
    public static EtlPublishedWorkflowFixture ReopenNativeUpgradeFixture(string rootDirectory,
        EtlV1NativeUpgradeHandoff handoff) =>
        new(rootDirectory, handoff.Scope, handoff.Bindings, handoff.OriginalArtifact, handoff.UpdatedArtifact,
            handoff.Executable, handoff.Reference, "runtime.db", preserveRootDirectory: true);
}

public sealed partial class EtlPublishedWorkflowJourneyTests
{
    private static readonly JsonSerializerOptions V1NativeUpgradeJson = new(JsonSerializerDefaults.Web);
    private const string V1NativeUpgradeHandoffFile = "native-upgrade-handoff.json";

    [Fact]
    public async Task V1_native_upgrade_producer_suspends_before_acceptance()
    {
        var dataRoot = NativeUpgradeDataRoot();
        var fixtureRoot = dataRoot;
        Assert.False(File.Exists(HandoffPath(dataRoot)));

        await using var published = await EtlPublishedWorkflowFixture.CreateAsync(
            flowchart: false,
            rootDirectory: fixtureRoot,
            runtimeDatabaseFileName: "runtime.db",
            preserveRootDirectory: true);
        var offlineDesignDatabase = published.EtlDesignDatabase + ".offline";
        File.Move(published.EtlDesignDatabase, offlineDesignDatabase);
        RuntimeHost? runtime = await RuntimeHost.CreateAsync(published);
        try
        {
            await runtime.AssertRuntimeOnlyAsync();
            await runtime.StartAsync();
            var invocation = await runtime.WaitForSuspensionAsync();
            Assert.Equal(published.OriginalArtifact.ArtifactId, invocation.Artifact.ArtifactId);
            Assert.Equal(published.OriginalArtifact.ContentHash, invocation.Artifact.ContentHash);
            Assert.Equal(published.Executable.Identity.ArtifactId, invocation.ResultAddress.ExecutableId);
            Assert.Equal(published.Executable.Identity.ArtifactHash, invocation.ResultAddress.ExecutableHash);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var child = await StartNativeUpgradeWorkerAsync(published, "predecessor",
                EtlWorkerStoreGate.BeforeAcceptance, deadline.Token);
            var receipt = await child.WaitForAcceptanceAsync(deadline.Token);
            Assert.Equal(child.ProcessId, receipt.ProcessId);
            Assert.NotEqual(Environment.ProcessId, receipt.ProcessId);

            var before = await ReadNativeUpgradeSnapshotAsync(runtime, invocation);
            Assert.Equal(EtlRunStatus.Running, before.Run.Status);
            Assert.Equal(0, before.Run.AcceptedRecords);
            Assert.Equal(receipt.Acceptance.Batch.EndOrdinalExclusive - receipt.Acceptance.Batch.StartOrdinal,
                before.Run.OutstandingRecords);
            Assert.Equal(receipt.Acceptance.Batch.InputBytes, before.Run.OutstandingBytes);
            Assert.NotNull(before.Run.Prepared);
            Assert.Empty(before.Manifest);
            var partition = Assert.Single(before.Partitions);
            Assert.Equal(receipt.Lease.Owner, partition.Owner);
            Assert.Equal(receipt.Lease.Fence, partition.Fence);
            Assert.Equal(receipt.Acceptance.Batch,
                Assert.IsType<EtlBatchReservation>(partition.OwedBatch).Batch);
            Assert.False(Assert.IsType<EtlBatchReservation>(partition.OwedBatch).Settled);

            var effect = Assert.Single(receipt.Acceptance.Effects);
            Assert.Equal(EtlOutputRoute.Main, effect.Route);
            var outputPath = Path.Combine(published.OutputRoot, effect.Chunk.Key);
            var outputBytes = await File.ReadAllBytesAsync(outputPath, deadline.Token);
            Assert.Equal("{\"name\":\"Ada\"}\n", Encoding.UTF8.GetString(outputBytes));
            var outputHash = Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant();
            Assert.Equal(effect.Chunk.ContentHash, outputHash);

            var native = await runtime.ReadNativeAsync();
            Assert.Equal(WorkflowExecutionStatus.Running, native.Workflow.Status);
            var runEtl = Assert.Single(native.Activities,
                state => state.Execution.ActivityType == RunEtl.ActivityType);
            Assert.Equal(ActivityExecutionStatus.Suspended, runEtl.Status);
            var following = Assert.Single(published.Executable.Nodes,
                node => node.ActivityType == typeof(Elsa.Activities.Primitives.Activities.WriteLine).FullName);
            Assert.DoesNotContain(native.Activities,
                state => state.Execution.ExecutableNodeId == following.ExecutableNodeId);

            await child.CrashAsync();
            var afterCrash = await ReadNativeUpgradeSnapshotAsync(runtime, invocation);
            Assert.Equal(JsonSerializer.Serialize(before.Run, V1NativeUpgradeJson),
                JsonSerializer.Serialize(afterCrash.Run, V1NativeUpgradeJson));
            Assert.Empty(afterCrash.Manifest);
            var persistedPartition = Assert.Single(afterCrash.Partitions);
            Assert.Equal(partition.OwedBatch, persistedPartition.OwedBatch);
            Assert.Equal(receipt.Lease.Owner, persistedPartition.Owner);
            Assert.Equal(receipt.Lease.Fence, persistedPartition.Fence);
            var persistedExpiry = Assert.IsType<DateTimeOffset>(persistedPartition.ExpiresAt);
            await runtime.DisposeAsync();
            runtime = null;
            Assert.False(File.Exists(published.EtlDesignDatabase));
            Assert.True(File.Exists(offlineDesignDatabase));

            var handoff = new EtlV1NativeUpgradeHandoff(
                invocation.ResultAddress.WorkflowExecutionId,
                published.Scope,
                published.Bindings,
                published.OriginalArtifact,
                published.UpdatedArtifact,
                published.Executable,
                published.Reference,
                invocation,
                Assert.IsType<EtlRunBindings>(before.Run.Bindings),
                Assert.IsType<EtlPreparedRun>(before.Run.Prepared),
                receipt.Lease with { ExpiresAt = persistedExpiry },
                receipt.Acceptance,
                outputHash);
            await File.WriteAllBytesAsync(HandoffPath(dataRoot),
                JsonSerializer.SerializeToUtf8Bytes(handoff, V1NativeUpgradeJson), deadline.Token);
        }
        catch (Exception exception)
        {
            if (runtime is not null)
                await runtime.PreserveFailureAsync(exception);
            throw;
        }
        finally
        {
            if (runtime is not null)
                await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task V1_native_upgrade_successor_completes_same_run()
    {
        var dataRoot = NativeUpgradeDataRoot();
        var handoff = await ReadNativeUpgradeHandoffAsync(dataRoot);
        // A copied root must not quietly keep reading the predecessor's original paths.
        Assert.Equal(JsonSerializer.Serialize(EtlNativeTestFileBindings.Create(handoff.Scope, dataRoot), V1NativeUpgradeJson),
            JsonSerializer.Serialize(handoff.Bindings, V1NativeUpgradeJson));
        var fixtureRoot = dataRoot;
        await using var published = EtlPublishedWorkflowFixture.ReopenNativeUpgradeFixture(fixtureRoot, handoff);
        RuntimeHost? runtime = await RuntimeHost.CreateAsync(published, retainedExecutionId: handoff.ExecutionId);
        try
        {
            await runtime.AssertRuntimeOnlyAsync();
            var invocation = await runtime.WaitForSuspensionAsync();
            Assert.Equal(handoff.Invocation.RunId, invocation.RunId);
            Assert.Equal(handoff.Invocation.ResultAddress, invocation.ResultAddress);
            Assert.Equal(JsonSerializer.Serialize(handoff.Invocation.Artifact, V1NativeUpgradeJson),
                JsonSerializer.Serialize(invocation.Artifact, V1NativeUpgradeJson));
            Assert.Equal(handoff.OriginalArtifact.ArtifactId, invocation.Artifact.ArtifactId);
            Assert.Equal(handoff.OriginalArtifact.ContentHash, invocation.Artifact.ContentHash);
            Assert.Equal(handoff.Reference.ArtifactId, invocation.ResultAddress.ExecutableId);
            Assert.Equal(handoff.Executable.Identity.ArtifactHash, invocation.ResultAddress.ExecutableHash);
            Assert.False(File.Exists(published.EtlDesignDatabase));
            Assert.True(File.Exists(published.EtlDesignDatabase + ".offline"));

            var before = await ReadNativeUpgradeSnapshotAsync(runtime, invocation);
            Assert.Equal(EtlRunStatus.Running, before.Run.Status);
            Assert.Equal(0, before.Run.AcceptedRecords);
            Assert.NotNull(before.Run.Prepared);
            Assert.Equal(handoff.RunBindings, before.Run.Bindings);
            Assert.Equal(handoff.Prepared, before.Run.Prepared);
            Assert.Equal(handoff.OldAcceptance.Batch.EndOrdinalExclusive - handoff.OldAcceptance.Batch.StartOrdinal,
                before.Run.OutstandingRecords);
            Assert.Equal(handoff.OldAcceptance.Batch.InputBytes, before.Run.OutstandingBytes);
            Assert.Empty(before.Manifest);
            var oldPartition = Assert.Single(before.Partitions);
            var owed = Assert.IsType<EtlBatchReservation>(oldPartition.OwedBatch);
            Assert.Equal(handoff.OldAcceptance.Batch, owed.Batch);
            Assert.False(owed.Settled);
            Assert.Equal(handoff.OldLease.Fence, oldPartition.Fence);
            Assert.Equal(handoff.OldLease.Owner, oldPartition.Owner);
            var persistedExpiry = Assert.IsType<DateTimeOffset>(oldPartition.ExpiresAt);
            Assert.Equal(handoff.OldLease.ExpiresAt, persistedExpiry);
            var oldEffect = Assert.Single(handoff.OldAcceptance.Effects);
            var oldOutput = await File.ReadAllBytesAsync(Path.Combine(published.OutputRoot, oldEffect.Chunk.Key));
            Assert.Equal(handoff.PhysicalOutputSha256,
                Convert.ToHexString(SHA256.HashData(oldOutput)).ToLowerInvariant());

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await WaitForLeaseExpiryAsync(persistedExpiry, deadline.Token);
            Assert.True(DateTimeOffset.UtcNow >= persistedExpiry);
            await using var successor = await StartNativeUpgradeWorkerAsync(published, "successor",
                EtlWorkerStoreGate.AfterAcceptanceCommit, deadline.Token);
            var committed = await successor.WaitForCommittedAcceptanceAsync(deadline.Token);
            Assert.Equal(EtlStoreStatus.Applied, committed.Result.Status);
            Assert.Equal(successor.ProcessId, committed.ProcessId);
            Assert.NotEqual(Environment.ProcessId, committed.ProcessId);
            Assert.True(committed.Lease.Fence > handoff.OldLease.Fence);
            Assert.Equal(handoff.OldLease.PartitionOrdinal, committed.Lease.PartitionOrdinal);
            Assert.Equal(handoff.OldAcceptance.Batch, committed.Acceptance.Batch);
            Assert.Equal(JsonSerializer.Serialize(handoff.OldAcceptance, V1NativeUpgradeJson),
                JsonSerializer.Serialize(committed.Acceptance, V1NativeUpgradeJson));
            var effect = Assert.Single(committed.Acceptance.Effects);
            Assert.Equal(oldEffect, effect);
            var outputPath = Path.Combine(published.OutputRoot, effect.Chunk.Key);
            var outputBytes = await File.ReadAllBytesAsync(outputPath, deadline.Token);
            Assert.Equal(handoff.PhysicalOutputSha256,
                Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant());
            Assert.Equal("{\"name\":\"Ada\"}\n", Encoding.UTF8.GetString(outputBytes));

            await successor.ReleaseAsync(deadline.Token);
            await WaitForNativeUpgradeRunSettlementAsync(runtime, invocation, deadline.Token);
            await successor.CrashAsync();
            await runtime.StartWorkerAsync();
            await runtime.WaitForCompletionAsync(invocation);

            var native = await runtime.ReadNativeAsync();
            Assert.Equal(WorkflowExecutionStatus.Completed, native.Workflow.Status);
            var child = Assert.Single(native.Activities,
                state => state.Execution.ActivityType == RunEtl.ActivityType);
            Assert.Equal(ActivityExecutionStatus.Completed, child.Status);
            var following = Assert.Single(published.Executable.Nodes,
                node => node.ActivityType == typeof(Elsa.Activities.Primitives.Activities.WriteLine).FullName);
            Assert.Equal(ActivityExecutionStatus.Completed,
                Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == following.ExecutableNodeId).Status);
            var delivery = Assert.Single(child.TriggerDeliveries ?? []);
            Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, delivery.Status);
            Assert.Equal(invocation.ResultAddress.RegistrationId, delivery.RegistrationId);
            await using var operation = await runtime.OpenScopeAsync();
            Assert.Null(await operation.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(runtime.ExecutionId, invocation.ResultAddress.BookmarkId));

            var final = await ReadNativeUpgradeSnapshotAsync(runtime, invocation);
            Assert.Equal(EtlRunStatus.Succeeded, final.Run.Status);
            Assert.Equal(1, final.Run.AcceptedRecords);
            Assert.Equal(0, final.Run.OutstandingRecords);
            Assert.Equal(0, final.Run.OutstandingBytes);
            Assert.Equal(EtlTerminalOutcome.Succeeded, final.Run.TerminalResult!.Outcome);
            Assert.Equal(effect, Assert.Single(final.Manifest).Effect);
            var completion = Assert.IsType<ActivityCompletion>(child.Completion);
            var result = Assert.IsType<JsonElement>(completion.Result.InlineValue)
                .Deserialize<RunEtlResult>(V1NativeUpgradeJson);
            Assert.Equal(new RunEtlResult(invocation.RunId, EtlTerminalOutcome.Succeeded, 1, 0, 0, null), result);
            Assert.Null(child.PrivateState);
            // WaitForCompletionAsync requires this run's sole outbox row to be acknowledged.
        }
        catch (Exception exception)
        {
            if (runtime is not null)
                await runtime.PreserveFailureAsync(exception);
            throw;
        }
        finally
        {
            if (runtime is not null)
                await runtime.DisposeAsync();
        }
    }

    private static string NativeUpgradeDataRoot() =>
        Path.GetFullPath(Environment.GetEnvironmentVariable("ETL_V1_NATIVE_UPGRADE_ROOT")
            ?? throw new InvalidOperationException("Set ETL_V1_NATIVE_UPGRADE_ROOT for the opt-in native upgrade test."));

    private static string HandoffPath(string root) => Path.Combine(root, V1NativeUpgradeHandoffFile);

    private static async Task<EtlV1NativeUpgradeHandoff> ReadNativeUpgradeHandoffAsync(string root)
    {
        await using var stream = File.OpenRead(HandoffPath(root));
        return await JsonSerializer.DeserializeAsync<EtlV1NativeUpgradeHandoff>(stream, V1NativeUpgradeJson)
            ?? throw new InvalidDataException("The native upgrade handoff is empty.");
    }

    private static async Task<EtlWorkerProcess> StartNativeUpgradeWorkerAsync(EtlPublishedWorkflowFixture published,
        string role, EtlWorkerStoreGate gate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(EtlWorkerProtocol.JournalDirectory(published.RootDirectory));
        var configurationPath = EtlWorkerProtocol.ConfigurationPath(published.RootDirectory, role);
        var configuration = new EtlWorkerConfiguration(published.RootDirectory, published.Scope,
            published.Bindings.ToList(), StoreGate: gate);
        await EtlWorkerProtocol.WriteConfigurationAsync(published.RootDirectory, role, configuration, cancellationToken);
        var worker = new EtlWorkerProcess(published.RootDirectory, configurationPath, role);
        try
        {
            await worker.StartAsync(cancellationToken);
            return worker;
        }
        catch
        {
            await worker.DisposeAsync();
            throw;
        }
    }

    private static async Task<EtlV1NativeUpgradeSnapshot> ReadNativeUpgradeSnapshotAsync(RuntimeHost runtime,
        EtlInvocation invocation)
    {
        await using var operation = await runtime.OpenScopeAsync();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(invocation.Scope, invocation.RunId));
        var partitions = await store.ListPartitionsAsync(invocation.Scope, invocation.RunId, -1, 10);
        var manifest = await store.ListManifestAsync(invocation.Scope, invocation.RunId, null, 10);
        return new EtlV1NativeUpgradeSnapshot(run, partitions, manifest);
    }

    private static async Task WaitForLeaseExpiryAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(100)
                ? TimeSpan.FromMilliseconds(25)
                : TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static async Task WaitForNativeUpgradeRunSettlementAsync(RuntimeHost runtime, EtlInvocation invocation,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadNativeUpgradeSnapshotAsync(runtime, invocation);
            if (snapshot.Run.TerminalResult is not null)
            {
                Assert.Equal(EtlRunStatus.Succeeded, snapshot.Run.Status);
                Assert.Equal(1, snapshot.Run.AcceptedRecords);
                Assert.Equal(0, snapshot.Run.OutstandingRecords);
                Assert.Equal(0, snapshot.Run.OutstandingBytes);
                Assert.Single(snapshot.Manifest);
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }
}
#endif
