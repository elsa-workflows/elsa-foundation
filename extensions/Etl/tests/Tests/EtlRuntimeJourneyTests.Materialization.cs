using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Persistence.EntityFramework;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Terminal_materialization_replays_both_routes_in_manifest_order_and_ignores_unaccepted_orphans()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n99,Unknown\n2,Bram\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, maximumPageSize: 1);
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 2, MaxBatchRecords = 1 }
        };
        var (invocation, terminal) = await PrepareAndExecutePartitionsAsync(files, host, pipeline, "materialized-routes", 1, 0);

        Assert.Equal(EtlRunStatus.BusinessError, terminal.Status);
        Assert.Equal(2, terminal.AcceptedRecords);
        Assert.Equal(1, terminal.RejectedRecords);

        var admission = new EtlRunAdmission(invocation, Assert.IsType<EtlRunBindings>(terminal.Bindings));
        var orphanIdentity = EtlRuntimeIdentity.OutputIdentity(admission, "unaccepted-orphan-batch", EtlOutputRoute.Main);
        await new FileChunkStore().PublishAsync(files.OutputRoot, orphanIdentity, 128,
            (destination, ct) => destination.WriteAsync("{\"name\":\"ORPHAN\"}\n"u8.ToArray(), ct));

        EtlMaterializedRun first;
        EtlMaterializedRun replay;
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var materializer = operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>();
            first = await materializer.MaterializeAsync(files.Scope, invocation.RunId);
            replay = await materializer.MaterializeAsync(files.Scope, invocation.RunId);
        }

        Assert.Equal(first, replay);
        Assert.Equal("materialized-routes", first.RunId);
        Assert.Equal(EtlTerminalOutcome.BusinessError, first.Outcome);
        Assert.False(first.IsPartial);
        Assert.Equal(2, first.AcceptedRecords);
        Assert.Equal(1, first.RejectedRecords);
        Assert.Equal(0, first.FilteredRecords);

        var main = await ReadMaterializedRouteAsync(files.OutputRoot, first.Main);
        Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n", main);
        Assert.DoesNotContain("ORPHAN", main);

        var errors = await ReadMaterializedRouteAsync(files.ErrorRoot, first.Errors);
        var errorLines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(errorLines);
        Assert.Equal("{\"recordOrdinal\":2,\"code\":\"etl.lookup-key-not-found\",\"field\":\"id\"}", errorLines[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_accepted_chunk_refuses_materialization_without_publishing_a_route(bool corrupt)
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), corrupt ? "corrupt-chunk" : "missing-chunk");
        await files.AdmitAsync(host, invocation);
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);
        var entry = Assert.Single(await ReadManifestAsync(host.Services, files.Scope, invocation.RunId));
        var path = Path.Combine(files.OutputRoot, entry.Effect.Chunk.Key);

        if (corrupt)
            await File.WriteAllTextAsync(path, "{\"name\":\"Ava\"}\n", new UTF8Encoding(false));
        else
            File.Delete(path);

        var filesBefore = Directory.GetFiles(files.OutputRoot, "*.jsonl").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        await using var operation = host.Services.CreateAsyncScope();
        var materializer = operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>();
        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await materializer.MaterializeAsync(files.Scope, invocation.RunId));

        Assert.Equal("etl.accepted-output-invalid", error.Code);
        Assert.Equal(terminal.TerminalResult, (await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
            .FindRunAsync(files.Scope, invocation.RunId))!.TerminalResult);
        Assert.Equal(filesBefore, Directory.GetFiles(files.OutputRoot, "*.jsonl").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Empty_terminal_run_materializes_no_route_files()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "empty-materialization");
        await files.AdmitAsync(host, invocation);
        await files.WaitForTerminalAsync(host, invocation.RunId);

        await using var operation = host.Services.CreateAsyncScope();
        var materialized = await operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>()
            .MaterializeAsync(files.Scope, invocation.RunId);

        Assert.Equal(EtlTerminalOutcome.Succeeded, materialized.Outcome);
        Assert.False(materialized.IsPartial);
        Assert.Null(materialized.Main);
        Assert.Null(materialized.Errors);
        Assert.Equal(0, materialized.AcceptedRecords);
        Assert.Equal(0, materialized.RejectedRecords);
        Assert.Equal(0, materialized.FilteredRecords);
        Assert.False(Directory.Exists(files.OutputRoot));
        Assert.False(Directory.Exists(files.ErrorRoot));
    }

    [Fact]
    public async Task Active_run_cannot_be_materialized()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "active-materialization");
        await files.AdmitAsync(host, invocation);

        await using var operation = host.Services.CreateAsyncScope();
        var run = Assert.IsType<EtlRunView>(await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
            .FindRunAsync(files.Scope, invocation.RunId));
        Assert.Null(run.TerminalResult);

        var materializer = operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>();
        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await materializer.MaterializeAsync(files.Scope, invocation.RunId));

        Assert.Equal("etl.output-not-terminal", error.Code);
        Assert.False(Directory.Exists(files.OutputRoot));

        var missing = await Assert.ThrowsAsync<EtlEffectException>(async () =>
        {
            await materializer.MaterializeAsync(files.Scope, "run-that-does-not-exist");
        });
        Assert.Equal("etl.run-not-found", missing.Code);
    }

    [Fact]
    public async Task Removed_pinned_output_revision_refuses_materialization_instead_of_using_current_alias()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var firstHost = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, bindingRevision: "output-v1");
        var invocation = files.Invocation(EtlTestPipelines.Create(), "removed-output-pin");
        await files.AdmitAsync(firstHost, invocation);
        await files.WaitForTerminalAsync(firstHost, invocation.RunId);
        await files.StopHostAsync(firstHost);

        var retargetedHost = await files.StartHostAsync(EfMigratePolicy.Validate, bindingRevision: "output-v2");
        await using var operation = retargetedHost.Services.CreateAsyncScope();
        var materializer = operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>();
        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await materializer.MaterializeAsync(files.Scope, invocation.RunId));

        Assert.Equal("etl.binding-unavailable", error.Code);
    }

    [Fact]
    public async Task Cancelled_run_materializes_only_prior_accepted_output_and_marks_it_partial()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n3,Cora\n4,Dara\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 2, MaxBatchRecords = 1 }
        };
        var (invocation, run) = await PrepareAndExecutePartitionsAsync(files, host, pipeline, "partial-cancel", 0);
        Assert.Equal(EtlRunStatus.Running, run.Status);
        Assert.Equal(2, run.AcceptedRecords);

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            var cancelled = await store.CancelAsync(invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            var materialized = await operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>()
                .MaterializeAsync(files.Scope, invocation.RunId);

            Assert.Equal(EtlTerminalOutcome.Cancelled, materialized.Outcome);
            Assert.True(materialized.IsPartial);
            Assert.Equal(2, materialized.AcceptedRecords);
            Assert.Equal(0, materialized.RejectedRecords);
            Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n",
                await ReadMaterializedRouteAsync(files.OutputRoot, materialized.Main));
            Assert.Null(materialized.Errors);
        }
    }

    [Fact]
    public async Task Conflicting_effect_fault_materializes_prior_accepted_output_as_partial()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n3,Cora\n4,Dara\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 2, MaxBatchRecords = 1 }
        };
        var (invocation, run) = await PrepareAndExecutePartitionsAsync(files, host, pipeline, "partial-fault", 0);
        Assert.Equal(EtlRunStatus.Running, run.Status);

        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var secondPartition = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(files.Scope, invocation.RunId, "materializer-conflict"));
        Assert.Equal(1, secondPartition.PartitionOrdinal);

        var prepared = await RebuildPreparationAsync(operation.ServiceProvider, run, pipeline);
        await using var builder = await EtlInputBatchBuilder.OpenAsync(
            operation.ServiceProvider.GetRequiredService<EtlRuntimeOptions>().SnapshotRoot, pipeline, prepared);
        var next = await builder.BuildNextAsync(secondPartition.PartitionOrdinal, secondPartition.NextOrdinal);
        var batch = Assert.IsType<EtlInputBatch>(next.Batch);
        var admission = new EtlRunAdmission(run.Invocation, Assert.IsType<EtlRunBindings>(run.Bindings));
        var descriptor = EtlRuntimeMetadata.Batch(admission, prepared, batch);
        var conflictKey = EtlRuntimeIdentity.OutputKey(admission, descriptor.BatchId, EtlOutputRoute.Main);
        await File.WriteAllTextAsync(Path.Combine(files.OutputRoot, conflictKey), "{\"name\":\"conflict\"}\n", new UTF8Encoding(false));

        await execution.ExecutePartitionAsync(secondPartition);
        var faulted = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId));
        Assert.Equal(EtlRunStatus.Faulted, faulted.Status);
        Assert.Equal("etl.effect-conflict", faulted.TerminalResult!.Diagnostic!.Code);

        var materialized = await operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>()
            .MaterializeAsync(files.Scope, invocation.RunId);

        Assert.Equal(EtlTerminalOutcome.Faulted, materialized.Outcome);
        Assert.True(materialized.IsPartial);
        Assert.Equal(2, materialized.AcceptedRecords);
        Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n",
            await ReadMaterializedRouteAsync(files.OutputRoot, materialized.Main));
        Assert.Null(materialized.Errors);
    }

    [Fact]
    public async Task Cancellation_before_admission_has_no_materialized_files_or_fabricated_binding()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "unadmitted-materialization");
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(invocation)).Status);
        var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId));
        Assert.Null(before.Bindings);

        var result = await operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>()
            .MaterializeAsync(files.Scope, invocation.RunId);

        Assert.Equal(EtlTerminalOutcome.Cancelled, result.Outcome);
        Assert.True(result.IsPartial);
        Assert.Null(result.Main);
        Assert.Null(result.Errors);
        Assert.Equal(before.TerminalResult, (await store.FindRunAsync(files.Scope, invocation.RunId))!.TerminalResult);
        Assert.False(Directory.Exists(files.OutputRoot));
        Assert.False(Directory.Exists(files.ErrorRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_other_scope_run_cannot_be_materialized(bool otherScope)
    {
        await using var files = new RuntimeFiles("id,name\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "scoped-materialization");
        await files.AdmitAsync(host, invocation);
        await files.WaitForTerminalAsync(host, invocation.RunId);
        await using var operation = host.Services.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>().MaterializeAsync(
                otherScope ? new EtlScope("another-tenant", files.Scope.PersistencePartition) : files.Scope,
                otherScope ? invocation.RunId : "missing-run"));

        Assert.Equal("etl.run-not-found", error.Code);
    }

    [Fact]
    public async Task A_changed_materialized_file_is_refused_and_never_overwritten_on_replay()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "conflicting-materialization");
        await files.AdmitAsync(host, invocation);
        await files.WaitForTerminalAsync(host, invocation.RunId);
        await using var operation = host.Services.CreateAsyncScope();
        var materializer = operation.ServiceProvider.GetRequiredService<EtlAcceptedOutputMaterializer>();
        var original = await materializer.MaterializeAsync(files.Scope, invocation.RunId);
        var path = Path.Combine(files.OutputRoot, Assert.IsType<EtlObjectReference>(original.Main).Key);
        const string changed = "{\"name\":\"Ava\"}\n";
        await File.WriteAllTextAsync(path, changed, new UTF8Encoding(false));

        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await materializer.MaterializeAsync(files.Scope, invocation.RunId));

        Assert.Equal("etl.effect-conflict", error.Code);
        Assert.Equal(changed, await File.ReadAllTextAsync(path));
    }

    private static async Task<(EtlInvocation Invocation, EtlRunView Run)> PrepareAndExecutePartitionsAsync(
        RuntimeFiles files, IHost host, EtlPipeline pipeline, string runId, params int[] executionOrder)
    {
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        var invocation = files.Invocation(pipeline, runId);
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "materializer-preparation"));
        await execution.PrepareAsync(preparation);
        var leases = new List<EtlPartitionLease>();
        for (var index = 0; index <= executionOrder.Max(); index++)
        {
            var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(files.Scope, runId, $"materializer-{index}"));
            Assert.Equal(index, lease.PartitionOrdinal);
            leases.Add(lease);
        }
        foreach (var ordinal in executionOrder)
            await execution.ExecutePartitionAsync(leases[ordinal]);
        return (invocation, Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, runId)));
    }

    private static async Task<EtlPreparedDescriptor> RebuildPreparationAsync(
        IServiceProvider services, EtlRunView run, EtlPipeline pipeline)
    {
        var bindings = services.GetRequiredService<HostFileBindings>();
        var preparedRun = Assert.IsType<EtlPreparedRun>(run.Prepared);
        var snapshots = new EtlInputSnapshots(run.Invocation.Scope, run.Invocation.RunId,
            EtlRuntimeMetadata.PinnedBinding(run.Invocation.Scope, run.Bindings!.Source, EtlFileRole.Source),
            EtlRuntimeMetadata.PinnedBinding(run.Invocation.Scope, run.Bindings.Lookup, EtlFileRole.Lookup),
            new EtlChunk(preparedRun.Source.Key, preparedRun.Source.ContentHash, preparedRun.Source.ByteLength),
            new EtlChunk(preparedRun.Lookup.Key, preparedRun.Lookup.ContentHash, preparedRun.Lookup.ByteLength));
        var result = await new EtlFilePreparer(bindings,
                services.GetRequiredService<EtlRuntimeOptions>().SnapshotRoot)
            .PrepareAsync(pipeline, snapshots);
        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        Assert.Equal(preparedRun, EtlRuntimeMetadata.Prepared(result.Prepared!));
        return result.Prepared!;
    }

    private static async Task<IReadOnlyList<EtlManifestEntry>> ReadManifestAsync(IServiceProvider services, EtlScope scope, string runId)
    {
        var store = services.GetRequiredService<IEtlRuntimeStore>();
        var result = new List<EtlManifestEntry>();
        EtlManifestPosition? after = null;
        while (true)
        {
            var page = await store.ListManifestAsync(scope, runId, after, 1);
            if (page.Count == 0)
                return result;
            result.AddRange(page);
            after = page[^1].Position;
        }
    }

    private static async Task<string> ReadMaterializedRouteAsync(string root, EtlObjectReference? reference)
    {
        var output = Assert.IsType<EtlObjectReference>(reference);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, output.Key));
        Assert.Equal(output.ByteLength, bytes.LongLength);
        Assert.Equal(output.ContentHash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        return Encoding.UTF8.GetString(bytes);
    }
}
