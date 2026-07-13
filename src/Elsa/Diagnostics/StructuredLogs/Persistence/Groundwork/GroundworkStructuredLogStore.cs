using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using global::Groundwork.DiagnosticRecords;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>
/// Adapts Elsa's structured-log store seam to Groundwork's specialized diagnostic-record contract.
/// Capture remains nonblocking: appends enter a bounded queue and complete only after Groundwork returns
/// the committed provider cursor. Provider cursor values remain opaque inside Elsa's versioned envelope.
/// </summary>
public sealed class GroundworkStructuredLogStore : IStructuredLogStore, IAsyncDisposable
{
    private const int BatchSize = 200;
    private const int MaxAppendAttempts = 9;
    private const string SequenceField = "sequence";
    private const string LevelField = "level";
    private const string CategoryKeyField = "categoryKey";
    private const string SourceKeyField = "sourceKey";
    private const string ReplayTokenField = "replayToken";
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDiagnosticRecordStore _store;
    private readonly StructuredLogStoreBinding _binding;
    private readonly DiagnosticStorageScope _scope;
    private readonly DiagnosticStreamId _stream;
    private readonly DiagnosticRecordStreamDefinition _definition;
    private readonly int _maxRecentQuerySize;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly Channel<PendingAppend> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<PendingAppend, byte> _accepted = new();
    private readonly object _drainGate = new();
    private Task? _drainLoop;
    private int _disposed;

    public GroundworkStructuredLogStore(
        IDiagnosticRecordStore store,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.StreamId);

        _store = store;
        _binding = binding;
        _scope = new(binding.TenantId, binding.ScopeId);
        _stream = new(binding.StreamId);
        _definition = CreateStreamDefinition(binding.StreamId);
        _maxRecentQuerySize = Math.Clamp(options.Value.MaxRecentQuerySize, 1, _definition.Limits.MaxQueryLimit);
        _shutdownDrainTimeout = options.Value.ShutdownDrainTimeout < TimeSpan.Zero
            ? TimeSpan.Zero
            : options.Value.ShutdownDrainTimeout;
        var capacity = Math.Max(options.Value.BufferCapacity, BatchSize) * 4;
        _channel = Channel.CreateBounded<PendingAppend>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        }, pending => Fail(pending, new StructuredLogsException("The structured log append was shed before commit.")));
    }

    /// <summary>The provider-neutral Groundwork schema used by first-party structured-log adapters.</summary>
    public static DiagnosticRecordStreamDefinition StreamDefinition { get; } = CreateStreamDefinition("structured-logs");

    /// <summary>Creates the shared schema for a host-selected logical diagnostic stream.</summary>
    public static DiagnosticRecordStreamDefinition CreateStreamDefinition(string streamId) => new(
        new(streamId),
        SchemaVersion: 1,
        LogicalStorageName: "elsa_structured_logs",
        Fields:
        [
            new(SequenceField, DiagnosticFieldType.Int64, DiagnosticFieldCardinality.Scalar,
                Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In, DiagnosticPredicateOperator.RangeInclusive),
                IsRequired: true),
            new(LevelField, DiagnosticFieldType.Int64, DiagnosticFieldCardinality.Scalar,
                Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In, DiagnosticPredicateOperator.RangeInclusive),
                IsRequired: true),
            StringKey(CategoryKeyField),
            StringKey(SourceKeyField),
            StringKey(ReplayTokenField)
        ],
        Limits: new(
            MaxBatchRecords: BatchSize,
            MaxPayloadBytes: 1_048_576,
            MaxRecordIdBytes: 64,
            MaxFieldsPerRecord: 5,
            MaxQueryLimit: 5_000,
            MaxPredicateNodes: 16,
            MaxPredicateValues: 16,
            MaxJsonDepth: 64),
        MaxOperationClockSkew: TimeSpan.FromMinutes(5),
        AppendIdempotencyWindow: TimeSpan.FromHours(1),
        TrimIdempotencyWindow: TimeSpan.FromHours(1),
        LogicalHighWaterField: SequenceField);

    public ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        StartDraining();

        var pending = new PendingAppend(entry, Guid.NewGuid().ToString("N"));
        _accepted.TryAdd(pending, 0);
        if (!_channel.Writer.TryWrite(pending))
            Fail(pending, new StructuredLogsException("The structured log store is not accepting appends."));
        return new(pending.Completion.Task.WaitAsync(cancellationToken));
    }

    public async Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
    {
        var statistics = await _store.InspectAsync(new(_scope, _stream), cancellationToken);
        return statistics.LifetimeLogicalHighWater is { } highWater
            ? long.Parse(highWater.CanonicalValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : 0;
    }

    public async Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(
        StructuredLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var limit = filter.MaxCount is { } requested
            ? Math.Clamp(requested, 0, _maxRecentQuerySize)
            : _maxRecentQuerySize;
        if (limit == 0)
            return [];

        var query = BuildQuery(filter, limit, DiagnosticRecordOrder.CursorDescending);
        var page = await _store.QueryAsync(query, cancellationToken);
        var entries = page.Records.Select(ToEntry).Where(filter.Matches).ToList();
        entries.Reverse();
        return entries;
    }

    public async Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default)
    {
        var page = await _store.QueryAsync(
            new DiagnosticRecordQuery(_scope, _stream, 1, DiagnosticRecordOrder.CursorDescending),
            cancellationToken);
        return page.Records.Count == 0 ? null : ToEntry(page.Records[0]).ReplayCursor;
    }

    public async Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        var limit = Math.Min(maxCount, _maxRecentQuerySize);

        try
        {
            DiagnosticRecord? anchor = null;
            if (afterCursor is { } cursor)
                anchor = await ValidateAnchorAsync(cursor, cancellationToken);

            var query = new DiagnosticRecordQuery(
                _scope,
                _stream,
                limit,
                DiagnosticRecordOrder.CursorAscending);
            if (anchor is not null)
            {
                var statistics = await _store.InspectAsync(new(_scope, _stream), cancellationToken);
                if (statistics.LifetimeCommittedCursorHighWater is not { } snapshotHighWater)
                    throw new StructuredLogReplayCursorUnavailableException();
                query = query with
                {
                    Continuation = new(
                        snapshotHighWater,
                        anchor.Cursor,
                        DiagnosticRequestFingerprint.ForQuery(query, _definition))
                };
            }

            var page = await _store.QueryAsync(query, cancellationToken);
            var scanned = page.Records;
            var entries = scanned.Select(ToEntry).Where(filter.Matches).ToArray();
            var next = scanned.Count == 0 ? afterCursor : ToEntry(scanned[^1]).ReplayCursor;
            return new(entries, next, page.Continuation is not null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StructuredLogReplayCursorUnavailableException)
        {
            throw;
        }
        catch
        {
            throw new StructuredLogReplayCursorUnavailableException();
        }
    }

    public async Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        var operationId = new DiagnosticOperationId(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
        await _store.TrimAsync(DiagnosticTrimRequest.Create(_scope, _stream, operationId, keepNewest), cancellationToken);
    }

    private void StartDraining()
    {
        if (Volatile.Read(ref _drainLoop) is not null)
            return;
        lock (_drainGate)
            _drainLoop ??= Task.Run(() => DrainAsync(_shutdown.Token));
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var batch = new List<PendingAppend>(BatchSize);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var pending))
                    batch.Add(pending);
                if (batch.Count > 0)
                    await CommitBatchAsync(batch.ToArray(), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            var error = new StructuredLogsException("The structured log append was canceled before commit.");
            foreach (var pending in batch)
                Fail(pending, error);
            while (_channel.Reader.TryRead(out var pending))
                Fail(pending, error);
        }
    }

    private async Task CommitBatchAsync(IReadOnlyList<PendingAppend> pending, CancellationToken cancellationToken)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var operationId = new DiagnosticOperationId(issuedAt, Guid.NewGuid().ToString("N"));
        var records = pending.Select(x => ToRecord(x.Entry, x.RecordToken)).ToArray();
        var request = DiagnosticRecordBatch.Create(_scope, _stream, operationId, records);
        Exception? failure = null;
        for (var attempt = 0; attempt < MaxAppendAttempts; attempt++)
        {
            try
            {
                var result = await _store.AppendAsync(request, cancellationToken);
                var byId = result.Records.ToDictionary(x => x.RecordId, StringComparer.Ordinal);
                foreach (var item in pending)
                {
                    if (!byId.TryGetValue(item.RecordToken, out var record))
                        throw new StructuredLogsException("Groundwork returned an incomplete structured log append result.");
                    Complete(item, ToEntry(record));
                }

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
                if (attempt + 1 < MaxAppendAttempts)
                    await Task.Delay(RetryDelay(attempt), cancellationToken);
            }
        }

        var error = new StructuredLogsException("The structured log append could not be committed.", failure!);
        foreach (var item in pending)
            Fail(item, error);
    }

    private async Task<DiagnosticRecord> ValidateAnchorAsync(
        StructuredLogReplayCursor cursor,
        CancellationToken cancellationToken)
    {
        if (!GroundworkReplayCursorCodec.TryDecode(cursor, _binding, out var parts))
            throw new StructuredLogReplayCursorUnavailableException();
        var page = await _store.QueryAsync(
            new(
                _scope,
                _stream,
                1,
                Predicate: DiagnosticRecordPredicate.Equal(ReplayTokenField, DiagnosticFieldValue.String(parts.RecordToken))),
            cancellationToken);
        var anchor = page.Records.SingleOrDefault();
        if (anchor is null ||
            !StringComparer.Ordinal.Equals(anchor.Cursor.Value, parts.ProviderPosition) ||
            !StringComparer.Ordinal.Equals(anchor.RecordId, parts.RecordToken) ||
            !StringComparer.Ordinal.Equals(ToEntry(anchor).SourceId, parts.EntrySourceId))
            throw new StructuredLogReplayCursorUnavailableException();
        return anchor;
    }

    private DiagnosticRecordQuery BuildQuery(
        StructuredLogFilter filter,
        int limit,
        DiagnosticRecordOrder order)
    {
        var predicates = new List<DiagnosticRecordPredicate>();
        if (filter.MinimumLevel is { } level)
            predicates.Add(DiagnosticRecordPredicate.RangeInclusive(
                LevelField,
                DiagnosticFieldValue.Int64((long)level),
                DiagnosticFieldValue.Int64((long)Microsoft.Extensions.Logging.LogLevel.None)));
        if (!string.IsNullOrEmpty(filter.Category))
            predicates.Add(DiagnosticRecordPredicate.Equal(CategoryKeyField, DiagnosticFieldValue.String(Hash(filter.Category))));
        if (!string.IsNullOrEmpty(filter.SourceId))
            predicates.Add(DiagnosticRecordPredicate.Equal(SourceKeyField, DiagnosticFieldValue.String(Hash(filter.SourceId))));

        DiagnosticRecordPredicate? predicate = predicates.Count switch
        {
            0 => null,
            1 => predicates[0],
            _ => new DiagnosticRecordPredicate.All(predicates)
        };
        return new(_scope, _stream, limit, order, Predicate: predicate);
    }

    private DiagnosticRecordInput ToRecord(StructuredLogEntry entry, string recordToken) => new(
        recordToken,
        entry.Timestamp,
        JsonSerializer.Serialize(entry with { ReplayCursor = null }, SerializerOptions),
        new Dictionary<string, IReadOnlyList<DiagnosticFieldValue>>(StringComparer.Ordinal)
        {
            [SequenceField] = [DiagnosticFieldValue.Int64(entry.Sequence)],
            [LevelField] = [DiagnosticFieldValue.Int64((long)entry.Level)],
            [CategoryKeyField] = [DiagnosticFieldValue.String(Hash(entry.Category))],
            [SourceKeyField] = [DiagnosticFieldValue.String(Hash(entry.SourceId))],
            [ReplayTokenField] = [DiagnosticFieldValue.String(recordToken)]
        });

    private StructuredLogEntry ToEntry(DiagnosticRecord record)
    {
        var entry = JsonSerializer.Deserialize<StructuredLogEntry>(record.Payload, SerializerOptions)
                    ?? throw new StructuredLogsException("Groundwork returned an invalid structured log payload.");
        return entry with
        {
            ReplayCursor = GroundworkReplayCursorCodec.Encode(
                _binding,
                entry.SourceId,
                record.RecordId,
                record.Cursor.Value)
        };
    }

    private static DiagnosticFieldDefinition StringKey(string name) => new(
        name,
        DiagnosticFieldType.String,
        DiagnosticFieldCardinality.Scalar,
        Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In),
        IsRequired: true,
        MaxStringBytes: 128);

    private static FrozenSet<DiagnosticPredicateOperator> Set(params DiagnosticPredicateOperator[] values) =>
        values.ToFrozenSet();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static TimeSpan RetryDelay(int attempt)
    {
        var delay = BaseRetryDelay * Math.Pow(2, attempt);
        return delay < TimeSpan.FromSeconds(5) ? delay : TimeSpan.FromSeconds(5);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _channel.Writer.TryComplete();
        var drainLoop = Volatile.Read(ref _drainLoop);
        if (drainLoop is not null)
        {
            try
            {
                await drainLoop.WaitAsync(_shutdownDrainTimeout);
            }
            catch (TimeoutException)
            {
                await _shutdown.CancelAsync();
                var error = new StructuredLogsException("The structured log append was canceled before commit.");
                foreach (var pending in _accepted.Keys)
                    Fail(pending, error);
            }
            catch
            {
                // DrainAsync's finally block settles accepted appends. Diagnostics disposal must not
                // turn a provider failure into a host-shutdown failure.
            }
        }

        if (drainLoop is null || drainLoop.IsCompleted)
            _shutdown.Dispose();
        else
            _ = drainLoop.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _shutdown,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private void Complete(PendingAppend pending, StructuredLogEntry entry)
    {
        if (pending.Completion.TrySetResult(entry))
            _accepted.TryRemove(pending, out _);
    }

    private void Fail(PendingAppend pending, Exception exception)
    {
        if (pending.Completion.TrySetException(exception))
            _accepted.TryRemove(pending, out _);
    }

    private sealed class PendingAppend(StructuredLogEntry entry, string recordToken)
    {
        public StructuredLogEntry Entry { get; } = entry;
        public string RecordToken { get; } = recordToken;
        public TaskCompletionSource<StructuredLogEntry> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
