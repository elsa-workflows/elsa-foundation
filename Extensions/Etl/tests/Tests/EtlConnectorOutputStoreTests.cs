using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_output_plan_is_stable_across_replay_and_partition_recovery()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("connector-output-recovery", new("tenant-a", "output-recovery"), CsvMixedRoutePipeline());
        var leases = await StartOutputCsvRunAsync(store, admission);
        var lease = leases[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);

        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: "mixed");
        var positionJson = await context.ConnectorPartitions.AsNoTracking()
            .Where(row => row.PartitionOrdinal == lease.PartitionOrdinal).Select(row => row.PositionJson).SingleAsync();
        var before = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        var retained = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);
        var replay = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);
        var conflict = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId,
            CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: "replacement"));
        var after = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var row = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Applied, retained.Status);
        Assert.Equal(output, retained.Output);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(output, replay.Output);
        Assert.Equal(EtlStoreStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Output);
        Assert.Equal(output, EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(row.OutputJson!));
        Assert.Equal(BodyBytes(output), row.OutputBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(output)), row.MetadataBytes - Encoding.UTF8.GetByteCount(row.DescriptorJson));
        Assert.Equal(BodyBytes(output), (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(positionJson, await context.ConnectorPartitions.AsNoTracking()
            .Where(partition => partition.PartitionOrdinal == lease.PartitionOrdinal).Select(partition => partition.PositionJson).SingleAsync());
        Assert.Equal(before.Run.AcceptedRecords, after.Run.AcceptedRecords);
        Assert.Equal(before.Run.RejectedRecords, after.Run.RejectedRecords);
        Assert.Equal(before.Run.FilteredRecords, after.Run.FilteredRecords);
        Assert.Equal(before.Run.OutstandingRecords, after.Run.OutstandingRecords);
        Assert.Equal(before.Run.OutstandingBytes, after.Run.OutstandingBytes);
        Assert.Equal(metadataBefore + Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(output)), await RetainedMetadataBytesAsync(context));

        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement-worker"));

        Assert.True(recovered.Fence > lease.Fence);
        Assert.Equal(lease.Position, recovered.Position);
        Assert.Equal(batch, recovered.OwedBatch!.Batch);
        Assert.Equal(output, recovered.OwedBatch.Output);
        var recoveredReplay = await recoveryStore.ReserveConnectorBatchAsync(recovered, batch);
        Assert.Equal(EtlStoreStatus.Replay, recoveredReplay.Status);
        Assert.Equal(output, recoveredReplay.Reservation!.Output);
        Assert.Equal(BodyBytes(output), (await recoveryContext.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(metadataBefore + Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(output)),
            await RetainedMetadataBytesAsync(recoveryContext));
    }

    [Fact]
    public async Task Http_connector_output_recovers_with_its_retained_page_and_batch()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("connector-http-output-recovery", new("tenant-a", "http-output-recovery"),
            httpShardIds: ["shard"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "http-output-worker"));
        var attached = await store.AttachTestHttpPageAsync(initialLease,
            RetainedPage(initialLease, "http-output-page", byteLength: 23, recordCount: 2, nextContinuation: "cursor-1"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 2, inputBytes: 7);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0,
            suffix: "http", errorKind: EtlConnectorKind.Http);

        var retained = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);
        Assert.Equal(EtlStoreStatus.Applied, retained.Status);
        Assert.Equal(output, retained.Output);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "http-output-recovery"));
        Assert.Equal(lease.Position, recovered.Position);
        Assert.Equal(batch, recovered.OwedBatch!.Batch);
        Assert.Equal(output, recovered.OwedBatch.Output);
    }

    [Fact]
    public async Task Fully_filtered_connector_output_retains_a_bodyless_plan()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("connector-output-filtered", new("tenant-a", "output-filtered"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = new EtlConnectorBatchOutput(0, 0, 2, null, null);

        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);
        var row = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(output, result.Output);
        Assert.Equal(0, row.OutputBytes);
        Assert.NotNull(row.OutputJson);
        Assert.Equal(output, EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(row.OutputJson!));
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync(partition =>
            partition.PartitionOrdinal == lease.PartitionOrdinal)).RetainedOutputBytes);
    }

    [Theory]
    [InlineData("algebra")]
    [InlineData("missing-main")]
    [InlineData("kind")]
    [InlineData("effect")]
    [InlineData("route-count")]
    [InlineData("body-length")]
    public async Task Connector_output_store_rejects_invalid_output_plans_atomically(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission($"connector-output-invalid-{defect}", new("tenant-a", $"output-invalid-{defect}"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var valid = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: defect);
        var main = valid.Main!;
        var invalid = defect switch
        {
            "algebra" => valid with { FilteredRecords = 1 },
            "missing-main" => valid with { Main = null },
            "kind" => valid with { Main = main with { Kind = EtlConnectorKind.Jsonl } },
            "effect" => valid with { Main = main with { EffectId = CorruptHash(main.EffectId) } },
            "route-count" => valid with
            {
                Main = main with
                {
                    RecordCount = 2,
                    EffectId = RouteEffectId(admission, batch, EtlOutputRoute.Main, main.Body, 2)
                }
            },
            "body-length" => valid with
            {
                Main = main with
                {
                    Body = main.Body with { ByteLength = 0 },
                    EffectId = RouteEffectId(admission, batch, EtlOutputRoute.Main,
                        main.Body with { ByteLength = 0 }, main.RecordCount)
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null)
        };
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, invalid);
        var row = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Null(result.Output);
        Assert.Null(row.OutputJson);
        Assert.Null(row.OutputDigest);
        Assert.Equal(0, row.OutputBytes);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync(partition =>
            partition.PartitionOrdinal == lease.PartitionOrdinal)).RetainedOutputBytes);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("cancelled")]
    public async Task Connector_output_store_refuses_a_stale_or_terminal_lease(string state)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission($"connector-output-refused-{state}", new("tenant-a", $"output-refused-{state}"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 2, rejected: 0, filtered: 0, suffix: state);
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        EtlStoreStatus expected;
        if (state == "stale")
        {
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
            var replacement = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                admission.Invocation.Scope, admission.Invocation.RunId, "replacement-worker"));
            Assert.True(replacement.Fence > lease.Fence);
            expected = EtlStoreStatus.LeaseLost;
        }
        else
        {
            Assert.Equal(EtlStoreStatus.Applied, (await runtime.CancelAsync(admission.Invocation)).Status);
            expected = EtlStoreStatus.Terminal;
        }

        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Output);
        Assert.Null((await context.ConnectorBatches.AsNoTracking().SingleAsync()).OutputJson);
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
    }

    [Fact]
    public async Task Connector_output_store_rolls_back_when_lease_expires_after_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("connector-output-expiry", new("tenant-a", "output-expiry"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 2, rejected: 0, filtered: 0, suffix: "expiry");
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        expire.Armed = true;
        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);
        context.ChangeTracker.Clear();

        Assert.Equal(EtlStoreStatus.LeaseLost, result.Status);
        var retained = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        Assert.Null(retained.OutputJson);
        Assert.Null(retained.OutputDigest);
        Assert.Equal(0, retained.OutputBytes);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync(partition =>
            partition.PartitionOrdinal == lease.PartitionOrdinal)).RetainedOutputBytes);
    }

    [Fact]
    public async Task Connector_output_body_budget_is_run_wide_and_replay_does_not_charge_it_twice()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = CsvMixedRoutePipeline();
        var pipeline = defaults with { Limits = defaults.Limits with { MaxRetainedOutputBytes = 8 } };
        var admission = ConnectorAdmission("connector-output-run-budget", new("tenant-a", "output-run-budget"), pipeline);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var leases = await StartOutputCsvRunAsync(store, admission);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var firstBatch = OutputTestBatch(admission, run.Prepared!, leases[0]);
        var secondBatch = OutputTestBatch(admission, run.Prepared!, leases[1]);

        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(leases[0], firstBatch)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(leases[1], secondBatch)).Status);
        var firstOutput = CreateOutput(admission, firstBatch, accepted: 2, rejected: 0, filtered: 0,
            suffix: "budget-a", mainBytes: 8);
        var secondOutput = CreateOutput(admission, secondBatch, accepted: 2, rejected: 0, filtered: 0,
            suffix: "budget-b", mainBytes: 8);

        var first = await store.RetainConnectorBatchOutputAsync(leases[0], firstBatch.BatchId, firstOutput);
        var afterFirst = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes;
        var replay = await store.RetainConnectorBatchOutputAsync(leases[0], firstBatch.BatchId, firstOutput);
        var afterReplay = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes;
        var refused = await store.RetainConnectorBatchOutputAsync(leases[1], secondBatch.BatchId, secondOutput);

        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(8, afterFirst);
        Assert.Equal(afterFirst, afterReplay);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, refused.Status);
        Assert.Null(refused.Output);
        Assert.Equal(8, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync(partition =>
            partition.PartitionOrdinal == leases[1].PartitionOrdinal)).RetainedOutputBytes);
        Assert.Null((await context.ConnectorBatches.AsNoTracking().SingleAsync(batch => batch.BatchId == secondBatch.BatchId)).OutputJson);
    }

    [Fact]
    public async Task Connector_batch_output_budget_counts_both_route_bodies_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = CsvMixedRoutePipeline();
        var pipeline = defaults with { Limits = defaults.Limits with { MaxOutputBatchBytes = 10 } };
        var admission = ConnectorAdmission("connector-output-batch-budget", new("tenant-a", "output-batch-budget"), pipeline);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0,
            suffix: "batch-budget", mainBytes: 6, errorBytes: 6);

        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);

        Assert.Equal(12, BodyBytes(output));
        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Output);
        Assert.Null((await context.ConnectorBatches.AsNoTracking().SingleAsync()).OutputJson);
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
    }

    [Fact]
    public async Task Connector_output_plan_metadata_is_charged_to_the_retained_metadata_quota()
    {
        var measurement = await MeasureOutputMetadataBoundaryAsync();

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = CsvMixedRoutePipeline();
        var pipeline = defaults with
        {
            Http = defaults.Http with { MaxRetainedMetadataBytes = measurement.Limit }
        };
        var admission = ConnectorAdmission("connector-output-metadata-quota", new("tenant-a", "output-metadata-quota"), pipeline);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: "metadata");
        var baseline = await RetainedMetadataBytesAsync(context);
        var planBytes = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(output));

        var result = await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output);

        Assert.Equal(measurement.Baseline, baseline);
        Assert.Equal(measurement.PlanBytes, planBytes);
        Assert.Equal(measurement.Limit, baseline + planBytes - 1);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Output);
        Assert.Null((await context.ConnectorBatches.AsNoTracking().SingleAsync()).OutputJson);
        Assert.Equal(baseline, await RetainedMetadataBytesAsync(context));
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedOutputBytes);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("digest")]
    [InlineData("accounting")]
    [InlineData("null-plan")]
    public async Task Connector_corrupt_output_plan_digest_or_accounting_is_rejected_on_recovery(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission($"connector-output-corrupt-{defect}", new("tenant-a", $"output-corrupt-{defect}"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 2, rejected: 0, filtered: 0, suffix: "corrupt");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));

        var retained = await context.ConnectorBatches.SingleAsync();
        switch (defect)
        {
            case "plan":
            {
                var corrupted = output with { Main = output.Main! with { EffectId = CorruptHash(output.Main.EffectId) } };
                retained.OutputJson = EtlConnectorRecordCodec.Write(corrupted);
                retained.OutputDigest = OutputMetadataDigest(retained.OutputJson);
                break;
            }
            case "digest":
                retained.OutputDigest = CorruptHash(retained.OutputDigest!);
                break;
            case "accounting":
                retained.OutputBytes++;
                break;
            case "null-plan":
            {
                const string nullJson = "null";
                var metadataDelta = Encoding.UTF8.GetByteCount(nullJson) - Encoding.UTF8.GetByteCount(retained.OutputJson!);
                retained.OutputJson = nullJson;
                retained.OutputDigest = OutputMetadataDigest(nullJson);
                retained.MetadataBytes = checked(retained.MetadataBytes + metadataDelta);
                var partition = await context.ConnectorPartitions.SingleAsync(row => row.PartitionOrdinal == lease.PartitionOrdinal);
                partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes + metadataDelta);
                partition.MetadataBytes = checked(partition.MetadataBytes + metadataDelta);
                var connector = await context.ConnectorRuns.SingleAsync();
                connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadataDelta);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(defect), defect, null);
        }
        await context.SaveChangesAsync();

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await recoveryStore.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "recovery-worker"));
    }

    private static async Task<IReadOnlyList<EtlConnectorPartitionLease>> StartOutputCsvRunAsync(
        IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission, long recordCount = 4)
    {
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(
            admission.Invocation.Scope, $"{admission.Invocation.RunId}-preparer"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(preparation,
            ConnectorPreparedFile(recordCount)));
        var leases = new List<EtlConnectorPartitionLease>();
        var pipeline = EtlConnectorRecordCodec.Pipeline(admission.Invocation);
        var partitionCount = checked((int)Math.Min(recordCount, pipeline.Limits.FilePartitionCount));
        for (var ordinal = 0; ordinal < partitionCount; ordinal++)
            leases.Add(Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                admission.Invocation.Scope, admission.Invocation.RunId, $"{admission.Invocation.RunId}-worker")));
        return leases;
    }

    private static EtlConnectorBatchDescriptor OutputTestBatch(EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, EtlConnectorPartitionLease lease, int recordCount = 2)
    {
        var position = Assert.IsType<EtlConnectorFilePosition>(lease.Position);
        return ConnectorBatch(admission, prepared, lease,
            new EtlConnectorFileBatchRange(position.NextOrdinal, checked(position.NextOrdinal + recordCount)),
            inputBytes: 11, inputHash: EtlRuntimeRecordCodec.HashText($"output-test-input-{lease.PartitionOrdinal}"));
    }

    private static EtlConnectorBatchOutput CreateOutput(EtlConnectorRunAdmission admission,
        EtlConnectorBatchDescriptor batch, long accepted, long rejected, long filtered,
        string suffix, long mainBytes = 17, long errorBytes = 19,
        EtlConnectorKind errorKind = EtlConnectorKind.Jsonl)
    {
        var main = accepted == 0 ? null : OutputRoute(admission, batch, EtlOutputRoute.Main,
            EtlConnectorKind.Http, $"{suffix}-main", EtlRuntimeRecordCodec.HashText($"{suffix}-main"), mainBytes, accepted);
        var errors = rejected == 0 ? null : OutputRoute(admission, batch, EtlOutputRoute.Error,
            errorKind, $"{suffix}-errors", EtlRuntimeRecordCodec.HashText($"{suffix}-errors"), errorBytes, rejected);
        return new(accepted, rejected, filtered, main, errors);
    }

    private static EtlConnectorRouteOutput OutputRoute(EtlConnectorRunAdmission admission,
        EtlConnectorBatchDescriptor batch, EtlOutputRoute route, EtlConnectorKind kind,
        string key, string contentHash, long byteLength, long records)
    {
        var body = new EtlObjectReference(key, contentHash, byteLength);
        return new(kind, body, records, RouteEffectId(admission, batch, route, body, records));
    }

    private static string RouteEffectId(EtlConnectorRunAdmission admission,
        EtlConnectorBatchDescriptor batch, EtlOutputRoute route, EtlObjectReference body, long records) =>
        EtlRuntimeIdentity.ConnectorEffectId(admission, batch.BatchId, route, body.ContentHash, body.ByteLength, records);

    private static long BodyBytes(EtlConnectorBatchOutput output) =>
        (output.Main?.Body.ByteLength ?? 0) + (output.Errors?.Body.ByteLength ?? 0);

    private static string CorruptHash(string hash) => (hash[0] == 'a' ? "b" : "a") + hash[1..];

    private static string OutputMetadataDigest(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static async Task<(long Limit, long Baseline, int PlanBytes)> MeasureOutputMetadataBoundaryAsync()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("connector-output-metadata-quota", new("tenant-a", "output-metadata-quota"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: "metadata");
        var baseline = await RetainedMetadataBytesAsync(context);
        var planBytes = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(output));
        return (checked(baseline + planBytes - 1), baseline, planBytes);
    }
}
