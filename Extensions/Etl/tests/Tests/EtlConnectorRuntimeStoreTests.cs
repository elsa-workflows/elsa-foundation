using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_admission_snapshots_pins_and_replays_only_the_same_revision()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var shards = new List<string> { "shard-a", "shard-b" };
        var admission = ConnectorAdmission("connector-pins", new EtlScope("tenant-a", "partition-pins"), httpShardIds: shards);

        var admitted = await connector.AdmitConnectorAsync(admission);

        Assert.Equal(EtlStoreStatus.Applied, admitted.Status);
        Assert.NotNull(admitted.Run);
        Assert.Null(admitted.Run.Run.Bindings);
        Assert.Equal(new[] { "shard-a", "shard-b" }, admitted.Run.Bindings.HttpShardIds);

        shards[0] = "caller-mutated";
        var stableReplayAdmission = admission with
        {
            Bindings = admission.Bindings with { HttpShardIds = ["shard-a", "shard-b"] }
        };
        var replay = await connector.AdmitConnectorAsync(stableReplayAdmission);
        var changedPin = await connector.AdmitConnectorAsync(stableReplayAdmission with
        {
            Bindings = stableReplayAdmission.Bindings with
            {
                Source = stableReplayAdmission.Bindings.Source with
                {
                    Revision = stableReplayAdmission.Bindings.Source.Revision with { Revision = "source-revision-2" }
                }
            }
        });
        var persisted = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);

        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(EtlStoreStatus.Conflict, changedPin.Status);
        Assert.Equal(new[] { "shard-a", "shard-b" }, persisted!.Bindings.HttpShardIds);
        Assert.Equal("source-revision-1", persisted.Bindings.Source.Revision.Revision);
    }

    [Fact]
    public async Task Legacy_preparation_skips_connector_runs_and_legacy_commit_cannot_consume_connector_lease()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var scope = new EtlScope("tenant-a", "partition-claims");
        var connectorAdmission = ConnectorAdmission("a-connector", scope);
        var fileAdmission = CreateAdmission("z-file", scope);

        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(connectorAdmission)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await runtime.AdmitAsync(fileAdmission)).Status);

        var fileLease = await runtime.ClaimPreparationAsync(scope, "file-preparer");
        var connectorLease = await connector.ClaimConnectorPreparationAsync(scope, "connector-preparer");
        Assert.NotNull(connectorLease);
        var legacyCommit = await runtime.CommitPreparationAsync(connectorLease!, EtlRuntimeFixtures.Prepared(2));
        var connectorAfter = await connector.FindConnectorRunAsync(scope, connectorAdmission.Invocation.RunId);

        Assert.Equal(fileAdmission.Invocation.RunId, fileLease!.RunId);
        Assert.Equal(EtlStoreStatus.Invalid, legacyCommit);
        Assert.Equal(EtlRunStatus.Preparing, connectorAfter!.Run.Status);
        Assert.Null(connectorAfter.Prepared);
        Assert.Empty(await context.PreparedRuns.ToListAsync());
        Assert.Empty(await context.Partitions.ToListAsync());
    }

    [Fact]
    public async Task Preadmission_refusal_cannot_terminalize_an_admitted_connector_run()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("connector-refusal", new EtlScope("tenant-a", "partition-refusal"));

        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var refusal = await runtime.RejectAdmissionAsync(admission.Invocation, new EtlRunDiagnostic("binding-unavailable"));
        var persisted = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);

        Assert.Equal(EtlStoreStatus.Conflict, refusal.Status);
        Assert.Null(refusal.Result);
        Assert.Equal(EtlRunStatus.Preparing, persisted!.Run.Status);
        Assert.Null(persisted.Run.TerminalResult);
        Assert.Empty(await context.Terminals.ToListAsync());
        Assert.Empty(await context.Outbox.ToListAsync());
    }

    [Fact]
    public async Task Http_partitions_keep_distinct_shards_and_expired_replacement_invalidates_failure_authority()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(10));
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("connector-shard-leases", new EtlScope("tenant-a", "partition-shard-leases"));
        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var prepLease = await connector.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer");
        Assert.NotNull(prepLease);
        Assert.Equal(EtlStoreStatus.Applied, await connector.CommitConnectorPreparationAsync(prepLease!, new(null, null)));

        var first = await connector.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-a");
        var second = await connector.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-b");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.PartitionOrdinal, second.PartitionOrdinal);
        Assert.NotEqual(
            Assert.IsType<EtlConnectorHttpPosition>(first.Position).Request.ShardId,
            Assert.IsType<EtlConnectorHttpPosition>(second.Position).Request.ShardId);

        var earlySuccess = await runtime.SettleTerminalAsync(new EtlTerminalProposal(
            admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));
        Assert.Equal(EtlStoreStatus.Conflict, earlySuccess.Status);
        Assert.Null(earlySuccess.Result);

        clock.Advance(TimeSpan.FromSeconds(11));
        var replacement = await connector.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-c");
        Assert.NotNull(replacement);
        Assert.Equal(first.PartitionOrdinal, replacement.PartitionOrdinal);
        Assert.True(replacement.Fence > first.Fence);

        var staleFailure = await runtime.SettleTerminalAsync(new EtlTerminalProposal(
            admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Faulted,
            new EtlRunDiagnostic("connector-failure"), PartitionOrdinal: first.PartitionOrdinal,
            PartitionOwner: first.Owner, PartitionFence: first.Fence));
        var persisted = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);

        Assert.Equal(EtlStoreStatus.LeaseLost, staleFailure.Status);
        Assert.Null(staleFailure.Result);
        Assert.Equal(EtlRunStatus.Running, persisted!.Run.Status);
        Assert.Null(persisted.Run.TerminalResult);

        var currentFailure = await runtime.SettleTerminalAsync(new EtlTerminalProposal(
            admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Faulted,
            new EtlRunDiagnostic("connector-failure"), PartitionOrdinal: replacement.PartitionOrdinal,
            PartitionOwner: replacement.Owner, PartitionFence: replacement.Fence));
        Assert.Equal(EtlStoreStatus.Applied, currentFailure.Status);
        Assert.Equal(EtlTerminalOutcome.Faulted, currentFailure.Result!.Outcome);
        Assert.Single(await context.Terminals.ToListAsync());
        Assert.Single(await context.Outbox.ToListAsync());
    }

    [Fact]
    public async Task Csv_preparation_without_lookup_creates_typed_file_ranges_for_mixed_destinations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var pipeline = CsvMixedRoutePipeline();
        var admission = ConnectorAdmission("connector-csv-ranges", new EtlScope("tenant-a", "partition-csv-ranges"), pipeline);

        Assert.Null(admission.Bindings.Lookup);
        Assert.Equal(EtlConnectorKind.Http, admission.Bindings.Main.Kind);
        Assert.Equal(EtlConnectorKind.Jsonl, admission.Bindings.Errors.Kind);
        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var prepLease = await connector.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer");
        Assert.NotNull(prepLease);

        var commit = await connector.CommitConnectorPreparationAsync(prepLease!, ConnectorPreparedFile(recordCount: 5));
        var run = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        var first = await connector.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-a");
        var second = await connector.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-b");

        Assert.Equal(EtlStoreStatus.Applied, commit);
        Assert.Equal(272, run!.RetainedInputBytes);
        Assert.Null(run.Prepared!.Lookup);
        var firstPosition = Assert.IsType<EtlConnectorFilePosition>(first!.Position);
        var secondPosition = Assert.IsType<EtlConnectorFilePosition>(second!.Position);
        Assert.Equal(new EtlConnectorFilePosition(1, 4, 1), firstPosition);
        Assert.Equal(new EtlConnectorFilePosition(4, 6, 4), secondPosition);
    }

    // Previously this test settled the empty run by hand, which hid that no production caller ever did (spec 1190 FR-011).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_file_preparation_settles_terminal_success_with_the_commit(bool jsonl)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var pipeline = jsonl ? JsonlMixedRoutePipeline() : CsvMixedRoutePipeline();
        var admission = ConnectorAdmission("connector-empty-file", new EtlScope("tenant-a", "partition-empty-file"), pipeline);
        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var prepLease = await connector.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer");
        Assert.NotNull(prepLease);
        var prepared = ConnectorPreparedFile(recordCount: 0, sourceByteLength: jsonl ? 0 : 128, jsonl: jsonl);

        var commit = await connector.CommitConnectorPreparationAsync(prepLease!, prepared);
        var replay = await connector.CommitConnectorPreparationAsync(prepLease!, prepared);
        var lateSuccess = await runtime.SettleTerminalAsync(new EtlTerminalProposal(
            admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));
        var persisted = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);

        Assert.Equal(EtlStoreStatus.Applied, commit);
        Assert.Equal(EtlStoreStatus.Replay, replay);
        Assert.NotEqual(EtlStoreStatus.Applied, lateSuccess.Status);
        Assert.Equal(EtlRunStatus.Succeeded, persisted!.Run.Status);
        Assert.Equal(EtlTerminalOutcome.Succeeded, persisted.Run.TerminalResult!.Outcome);
        Assert.Single(await context.Terminals.ToListAsync());
        Assert.Single(await context.Outbox.ToListAsync());
        Assert.Empty(await context.ConnectorPartitions.ToListAsync());
    }

    [Fact]
    public async Task Jsonl_preparation_creates_file_ranges_and_bounds_its_sentinel_index()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var connector = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var scope = new EtlScope("tenant-a", "partition-jsonl");
        var admission = ConnectorAdmission("connector-jsonl", scope, JsonlMixedRoutePipeline());
        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var lease = await connector.ClaimConnectorPreparationAsync(scope, "preparer");

        // One unterminated one-byte record: two index entries exceed sourceBytes × 24, which only CSV forbids.
        Assert.Equal(EtlStoreStatus.Applied, await connector.CommitConnectorPreparationAsync(lease!,
            ConnectorPreparedFile(recordCount: 1, sourceByteLength: 1, jsonl: true)));
        var partition = await connector.ClaimConnectorPartitionAsync(scope, admission.Invocation.RunId, "worker");
        Assert.Equal(new EtlConnectorFilePosition(1, 2, 1), Assert.IsType<EtlConnectorFilePosition>(partition!.Position));
        // Two requested partitions over one record make exactly one partition.
        Assert.Null(await connector.ClaimConnectorPartitionAsync(scope, admission.Invocation.RunId, "second-worker"));
        Assert.Single(await context.ConnectorPartitions.ToListAsync());
        Assert.Equal(EtlRunStatus.Running, (await connector.FindConnectorRunAsync(scope, admission.Invocation.RunId))!.Run.Status);
    }

    [Theory]
    [InlineData("index-bound")]
    [InlineData("digest")]
    [InlineData("shards")]
    public async Task Jsonl_storage_refuses_invalid_preparation_and_bindings(string variant)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var connector = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var scope = new EtlScope("tenant-a", "partition-jsonl-invalid");
        var admission = ConnectorAdmission("connector-jsonl-invalid", scope, JsonlMixedRoutePipeline(),
            httpShardIds: variant == "shards" ? ["shard-a"] : null);
        if (variant == "digest")
            admission = admission with
            {
                Bindings = admission.Bindings with
                {
                    Source = admission.Bindings.Source with { Revision = admission.Bindings.Source.Revision with { ExpectedSha256 = null } }
                }
            };
        var admitted = await connector.AdmitConnectorAsync(admission);
        if (variant is "digest" or "shards")
        {
            Assert.Equal(EtlStoreStatus.Invalid, admitted.Status);
            return;
        }

        Assert.Equal(EtlStoreStatus.Applied, admitted.Status);
        var lease = await connector.ClaimConnectorPreparationAsync(scope, "preparer");
        Assert.Equal(EtlStoreStatus.Invalid, await connector.CommitConnectorPreparationAsync(lease!,
            ConnectorPreparedFile(recordCount: 2, sourceByteLength: 1, jsonl: true)));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("reference")]
    [InlineData("limit")]
    [InlineData("index")]
    public async Task Invalid_connector_prepared_input_leaves_no_durable_readiness_or_partitions(string failure)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var pipeline = CsvMixedRoutePipeline(maxSnapshotBytes: 128);
        var admission = ConnectorAdmission($"connector-invalid-{failure}", new EtlScope("tenant-a", $"partition-invalid-{failure}"), pipeline);
        Assert.Equal(EtlStoreStatus.Applied, (await connector.AdmitConnectorAsync(admission)).Status);
        var prepLease = await connector.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer");
        Assert.NotNull(prepLease);

        var prepared = ConnectorPreparedFile(recordCount: 2);
        var file = prepared.File!;
        prepared = failure switch
        {
            "hash" => prepared with { File = file with { Source = file.Source with { ContentHash = new string('b', 64) } } },
            "reference" => prepared with { File = file with { Source = file.Source with { Key = "" } } },
            "limit" => prepared with { File = file with { Source = file.Source with { ByteLength = 129 } } },
            "index" => prepared with { File = file with { Index = file.Index with { ByteLength = file.Index.ByteLength + 1 } } },
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };

        var status = await connector.CommitConnectorPreparationAsync(prepLease!, prepared);
        var persisted = await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);

        Assert.Equal(EtlStoreStatus.Invalid, status);
        Assert.Equal(EtlRunStatus.Preparing, persisted!.Run.Status);
        Assert.Null(persisted.Prepared);
        Assert.Equal(0, persisted.RetainedInputBytes);
        Assert.Empty(await context.ConnectorPartitions.ToListAsync());
    }

    private static EtlConnectorRunAdmission ConnectorAdmission(string runId, EtlScope scope,
        EtlPipelineV2? pipeline = null, IReadOnlyList<string>? httpShardIds = null) =>
        EtlRuntimeFixtures.ConnectorAdmission(runId, scope, pipeline, httpShardIds);

    private static EtlConnectorPreparedRun ConnectorPreparedFile(long recordCount = 4,
        long sourceByteLength = 128, long? indexByteLength = null, bool jsonl = false) =>
        new(new EtlPreparedCsvInput(
            new EtlObjectReference(jsonl ? "source.jsonl" : "source.csv", EtlRuntimeFixtures.Hash, sourceByteLength),
            new EtlObjectReference("source.index", EtlRuntimeFixtures.Hash, indexByteLength ?? checked((recordCount + 1) * 24)),
            recordCount), null);

    private static EtlPipelineV2 CsvMixedRoutePipeline(long maxSnapshotBytes = 1024L * 1024 * 1024)
    {
        var pipeline = EtlV2TestPipelines.Create();
        return pipeline with
        {
            Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, Required: true)]),
            Lookup = null,
            Routes = new EtlRoutes(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")),
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")],
            Limits = pipeline.Limits with
            {
                FilePartitionCount = 2,
                MaxPartitions = 8,
                MaxSnapshotBytes = maxSnapshotBytes,
                MaxLookupBytes = Math.Min(pipeline.Limits.MaxLookupBytes, maxSnapshotBytes)
            }
        };
    }

    private static EtlPipelineV2 JsonlMixedRoutePipeline() => CsvMixedRoutePipeline() with
    {
        Source = new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true)])
    };
}
