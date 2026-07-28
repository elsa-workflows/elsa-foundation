using Groundwork.DiagnosticRecords;
using Groundwork.Sqlite.DiagnosticRecords;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDurableOperationConformanceTests : IAsyncLifetime
{
    private static readonly DiagnosticStorageScope Scope = new("tenant-a", "diagnostics-a");
    private static readonly DiagnosticStreamId Stream = new("diagnostics-conformance");
    private static readonly DiagnosticRecordStreamDefinition Definition = new(
        Stream,
        SchemaVersion: 1,
        LogicalStorageName: "elsa_diagnostics_conformance",
        Fields:
        [
            new(
                "sequence",
                DiagnosticFieldType.Int64,
                DiagnosticFieldCardinality.Scalar,
                new HashSet<DiagnosticPredicateOperator> { DiagnosticPredicateOperator.Equal },
                IsRequired: true)
        ],
        Limits: new(
            MaxBatchRecords: 2,
            MaxPayloadBytes: 64,
            MaxRecordIdBytes: 64,
            MaxFieldsPerRecord: 1,
            MaxQueryLimit: 10,
            MaxPredicateNodes: 4),
        MaxOperationClockSkew: TimeSpan.FromMinutes(5),
        AppendIdempotencyWindow: TimeSpan.FromHours(1),
        TrimIdempotencyWindow: TimeSpan.FromHours(1),
        LogicalHighWaterField: "sequence");

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"elsa-diagnostics-operations-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Append_replay_is_idempotent_and_conflicting_identity_reuse_does_not_mutate()
    {
        var store = await CreateStoreAsync();
        var request = Batch("idempotent", Record("record-1", 1));

        var committed = await store.AppendAsync(request);
        var replayed = await store.AppendAsync(request);
        var conflicting = DiagnosticRecordBatch.Create(
            Scope,
            Stream,
            request.OperationId,
            [Record("record-2", 2)]);
        await Assert.ThrowsAsync<DiagnosticOperationConflictException>(async () =>
            await store.AppendAsync(conflicting));

        var retained = await store.QueryAsync(new(Scope, Stream, 10));
        Assert.Equal(DiagnosticAppendStatus.Committed, committed.Status);
        Assert.Equal(DiagnosticAppendStatus.Replayed, replayed.Status);
        Assert.Equal(committed.Records.Select(x => x.Cursor), replayed.Records.Select(x => x.Cursor));
        Assert.Equal("record-1", Assert.Single(retained.Records).RecordId);
    }

    [Fact]
    public async Task Acknowledgement_loss_retry_returns_the_original_outcome_without_duplication()
    {
        var provider = await CreateStoreAsync();
        var store = new AcknowledgementLosingStore(provider);
        var request = Batch("acknowledgement-loss", Record("record-1", 1));

        await Assert.ThrowsAsync<DiagnosticAcknowledgementLostException>(async () =>
            await store.AppendAsync(request));
        var replayed = await store.AppendAsync(request);
        var retained = await provider.QueryAsync(new(Scope, Stream, 10));

        Assert.Equal(2, store.AppendCalls);
        Assert.Equal(DiagnosticAppendStatus.Replayed, replayed.Status);
        Assert.Equal("record-1", Assert.Single(retained.Records).RecordId);
    }

    [Fact]
    public async Task Cancellation_before_or_during_provider_work_does_not_mutate()
    {
        var provider = await CreateStoreAsync();
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await provider.AppendAsync(Batch("cancelled-before", Record("record-before", 1)), cancelled.Token));
        }

        var blocking = new CancellationBoundaryStore(provider);
        using var during = new CancellationTokenSource();
        var append = blocking.AppendAsync(
            Batch("cancelled-during", Record("record-during", 2)),
            during.Token).AsTask();
        await blocking.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await during.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => append);

        var inspection = await provider.InspectAsync(new(Scope, Stream));
        Assert.Equal(0, inspection.RetainedCount.Value);
    }

    [Fact]
    public async Task Concurrent_writers_commit_distinct_records_in_one_durable_order()
    {
        var first = await CreateStoreAsync();
        var second = await CreateStoreAsync();

        var results = await Task.WhenAll(
            first.AppendAsync(Batch("writer-a", Record("record-a", 1))).AsTask(),
            second.AppendAsync(Batch("writer-b", Record("record-b", 2))).AsTask());
        var retained = await first.QueryAsync(new(Scope, Stream, 10));

        Assert.Equal(2, results.SelectMany(x => x.Records).Select(x => x.Cursor).Distinct().Count());
        Assert.Equal(["record-a", "record-b"], retained.Records.Select(x => x.RecordId).Order());
        Assert.Equal(["1", "2"], retained.Records.Select(x => x.Cursor.Value));
    }

    [Fact]
    public async Task Malformed_payload_and_oversized_batch_are_rejected_without_mutation()
    {
        var store = await CreateStoreAsync();
        var malformed = Batch("malformed", Record("record-malformed", 1) with { Payload = "not-json" });
        var oversized = Batch(
            "oversized",
            Record("record-1", 1),
            Record("record-2", 2),
            Record("record-3", 3));

        var malformedError = await Assert.ThrowsAsync<DiagnosticRecordValidationException>(async () =>
            await store.AppendAsync(malformed));
        var oversizedError = await Assert.ThrowsAsync<DiagnosticRecordValidationException>(async () =>
            await store.AppendAsync(oversized));
        var inspection = await store.InspectAsync(new(Scope, Stream));

        Assert.Contains(malformedError.Errors, x => x.Code == "append.payload.invalid_json");
        Assert.Contains(oversizedError.Errors, x => x.Code == "append.batch.too_large");
        Assert.Equal(0, inspection.RetainedCount.Value);
    }

    private Task<SqliteDiagnosticRecordStore> CreateStoreAsync() =>
        SqliteDiagnosticRecordStoreFactory.CreateAsync(
            $"Data Source={_databasePath}",
            Definition);

    private static DiagnosticRecordBatch Batch(
        string operationNonce,
        params DiagnosticRecordInput[] records) =>
        DiagnosticRecordBatch.Create(
            Scope,
            Stream,
            new(DateTimeOffset.UtcNow, operationNonce),
            records);

    private static DiagnosticRecordInput Record(string id, long sequence) => new(
        id,
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        $"{{\"sequence\":{sequence}}}",
        new Dictionary<string, IReadOnlyList<DiagnosticFieldValue>>(StringComparer.Ordinal)
        {
            ["sequence"] = [DiagnosticFieldValue.Int64(sequence)]
        });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private sealed class AcknowledgementLosingStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private int _loseNext = 1;
        public int AppendCalls { get; private set; }
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            var result = await inner.AppendAsync(batch, cancellationToken);
            if (Interlocked.Exchange(ref _loseNext, 0) == 1)
                throw new DiagnosticAcknowledgementLostException(
                    DiagnosticOperationKind.Append,
                    batch.Stream,
                    batch.OperationId);
            return result;
        }
    }

    private sealed class CancellationBoundaryStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await inner.AppendAsync(batch, cancellationToken);
        }
    }
}
