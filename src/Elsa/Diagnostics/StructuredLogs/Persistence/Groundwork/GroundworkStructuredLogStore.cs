using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Persistence.Groundwork.Composition;
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
public sealed class GroundworkStructuredLogStore :
    IStructuredLogStore,
    IDiagnosticsPersistenceDrain,
    IDiagnosticsPersistenceStartupResource,
    IAsyncDisposable
{
    private const int BatchSize = 200;
    private const int MaxAppendAttempts = 9;
    private const int DefaultMaxRetainedEntries = 100_000;
    private const int DefaultRetentionInterval = 5_000;
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
    private readonly DiagnosticsDrain<PendingAppend, StructuredLogEntry> _drain;
    private readonly StructuredLogStartupResource? _startupResource;
    private int _disposed;

    /// <summary>
    /// Creates the host-composed adapter without performing provider I/O. The shared diagnostics
    /// lifecycle admits and acquires the declared stream before starting this store's capture drain.
    /// </summary>
    public GroundworkStructuredLogStore(
        IDiagnosticRecordStoreSessionFactory sessionFactory,
        IDiagnosticRecordDeploymentManifestSource deploymentSource,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        IDiagnosticsPersistenceObserver? observer = null)
        : this(
            new GroundworkDiagnosticRecordStoreGate(),
            sessionFactory,
            deploymentSource,
            options,
            binding,
            observer)
    {
    }

    private GroundworkStructuredLogStore(
        GroundworkDiagnosticRecordStoreGate storeGate,
        IDiagnosticRecordStoreSessionFactory sessionFactory,
        IDiagnosticRecordDeploymentManifestSource deploymentSource,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        IDiagnosticsPersistenceObserver? observer)
        : this(storeGate, options, binding, observer)
    {
        _startupResource = new(
            storeGate,
            sessionFactory,
            deploymentSource,
            binding);
    }

    public GroundworkStructuredLogStore(
        IDiagnosticRecordStore store,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        IDiagnosticsPersistenceObserver? observer = null)
        : this(
            store,
            options,
            binding,
            DefaultMaxRetainedEntries,
            DefaultRetentionInterval,
            observer)
    {
    }

    /// <summary>
    /// Creates a durable store with explicit provider-side retention bounds. The overload is intended for
    /// hosts that need a retention policy different from the first-party defaults.
    /// </summary>
    public GroundworkStructuredLogStore(
        IDiagnosticRecordStore store,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        int maxRetainedEntries,
        int retentionInterval,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ValidateDiagnosticIdentifier(binding.TenantId, nameof(binding.TenantId));
        ValidateDiagnosticIdentifier(binding.ScopeId, nameof(binding.ScopeId));
        ValidateDiagnosticIdentifier(binding.StreamId, nameof(binding.StreamId));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionInterval, 1);

        _store = store;
        _binding = binding;
        _scope = new(binding.TenantId, binding.ScopeId);
        _stream = new(binding.StreamId);
        _definition = CreateStreamDefinition(binding.StreamId);
        _maxRecentQuerySize = Math.Clamp(options.Value.MaxRecentQuerySize, 1, _definition.Limits.MaxQueryLimit);
        var capacity = Math.Max(options.Value.BufferCapacity, BatchSize) * 4;
        var shutdownTimeout = options.Value.ShutdownDrainTimeout <= TimeSpan.Zero
            ? TimeSpan.FromTicks(1)
            : options.Value.ShutdownDrainTimeout;
        _drain = new DiagnosticsDrain<PendingAppend, StructuredLogEntry>(
            new GroundworkStructuredLogDrainTarget(_store, _scope, _stream, ToRecord, ToEntry, maxRetainedEntries),
            new DiagnosticsDrainOptions
            {
                BatchSize = BatchSize,
                QueueCapacity = capacity,
                RetentionInterval = retentionInterval,
                MaxAttempts = MaxAppendAttempts,
                BaseRetryDelay = BaseRetryDelay,
                MaxRetryDelay = TimeSpan.FromSeconds(5),
                ShutdownTimeout = shutdownTimeout
            },
            observer);
    }

    /// <summary>Starts the host-owned capture drain. Repeated starts while running are idempotent.</summary>
    public void Start() => _drain.Start();

    ValueTask<IDiagnosticsPersistenceResourceLease> IDiagnosticsPersistenceStartupResource.AcquireAsync(
        CancellationToken cancellationToken) =>
        _startupResource?.AcquireAsync(cancellationToken)
        ?? ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(
            DirectStoreResourceLease.Instance);

    /// <summary>The provider-neutral Groundwork schema used by first-party structured-log adapters.</summary>
    public static DiagnosticRecordStreamDefinition StreamDefinition { get; } = CreateStreamDefinition("structured-logs");

    /// <summary>Creates the shared schema for a host-selected logical diagnostic stream.</summary>
    public static DiagnosticRecordStreamDefinition CreateStreamDefinition(string streamId)
    {
        ValidateDiagnosticIdentifier(streamId, nameof(streamId));
        return new(
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
    }

    public async ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_drain.State == DiagnosticsDrainState.Created)
        {
            throw new InvalidOperationException(
                "The Groundwork structured-log capture drain must be started by the host lifecycle before use.");
        }
        try
        {
            return await _drain.EnqueueAsync(
                new PendingAppend(entry, Guid.NewGuid().ToString("N")),
                cancellationToken);
        }
        catch (DiagnosticsDrainException exception)
        {
            throw new StructuredLogsException("The structured log append could not be committed.", exception);
        }
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

    public async Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        var operationId = new DiagnosticOperationId(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
        await _store.TrimAsync(DiagnosticTrimRequest.Create(_scope, _stream, operationId, keepNewest), cancellationToken);
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

    private static void ValidateDiagnosticIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DiagnosticStringComparisonKey.IsAsciiIgnoreCaseValue(value) ||
            char.IsWhiteSpace(value[0]) ||
            char.IsWhiteSpace(value[^1]) ||
            Encoding.UTF8.GetByteCount(value) > 64)
        {
            throw new ArgumentException(
                "Groundwork diagnostic identifiers must use printable ASCII, have no edge whitespace, and contain at most 64 UTF-8 bytes.",
                parameterName);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _disposed, 1);
        await _drain.StopIfStartedAsync(cancellationToken);
    }

    Task IDiagnosticsPersistenceDrain.StopAsync(CancellationToken cancellationToken) =>
        StopAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed record PendingAppend(StructuredLogEntry Entry, string RecordToken);

    private sealed class GroundworkStructuredLogDrainTarget(
        IDiagnosticRecordStore store,
        DiagnosticStorageScope scope,
        DiagnosticStreamId stream,
        Func<StructuredLogEntry, string, DiagnosticRecordInput> toRecord,
        Func<DiagnosticRecord, StructuredLogEntry> toEntry,
        int maxRetainedEntries)
        : IDiagnosticsDrainTarget<PendingAppend, StructuredLogEntry>
    {
        // The 49-byte nonce remains within SQL Server's 64-byte diagnostic identifier bound.
        private const string RetentionOperationPrefix = "elsa-slog-ret-v1:";
        private readonly object _retentionOperationGate = new();
        private DiagnosticOperationId? _retentionOperation;

        public async ValueTask<DiagnosticsDrainCommit<StructuredLogEntry>> CommitAsync(
            DiagnosticsDrainBatch<PendingAppend> batch,
            CancellationToken cancellationToken = default)
        {
            var request = DiagnosticRecordBatch.Create(
                scope,
                stream,
                new DiagnosticOperationId(batch.Id.IssuedAt, $"structured-logs-v1:{batch.Id}"),
                batch.Items.Select(item => toRecord(item.Entry, item.RecordToken)).ToArray());
            var result = await store.AppendAsync(request, cancellationToken);
            var byId = result.Records.ToDictionary(record => record.RecordId, StringComparer.Ordinal);
            var entries = batch.Items.Select(item =>
                byId.TryGetValue(item.RecordToken, out var record)
                    ? toEntry(record)
                    : throw new StructuredLogsException("Groundwork returned an incomplete structured log append result."))
                .ToArray();
            return new DiagnosticsDrainCommit<StructuredLogEntry>(entries, entries.Length);
        }

        public async ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticOperationId operationId;
            lock (_retentionOperationGate)
            {
                operationId = _retentionOperation ??= new(
                    DateTimeOffset.UtcNow,
                    $"{RetentionOperationPrefix}{Guid.NewGuid():N}");
            }

            var result = await store.TrimAsync(
                DiagnosticTrimRequest.Create(
                    scope,
                    stream,
                    operationId,
                    maxRetainedEntries),
                cancellationToken);

            // A successful provider response is authoritative. Until then, retain the exact operation
            // identity so an acknowledgement-loss retry replays the durable trim instead of trimming anew.
            lock (_retentionOperationGate)
            {
                if (_retentionOperation == operationId)
                    _retentionOperation = null;
            }

            return result.DeletedCount.Value > int.MaxValue
                ? int.MaxValue
                : (int)result.DeletedCount.Value;
        }
    }

    private sealed class StructuredLogStartupResource(
        GroundworkDiagnosticRecordStoreGate storeGate,
        IDiagnosticRecordStoreSessionFactory sessionFactory,
        IDiagnosticRecordDeploymentManifestSource deploymentSource,
        StructuredLogStoreBinding binding)
    {
        private readonly Lock _gate = new();
        private ExceptionDispatchInfo? _failure;
        private StructuredLogResourceLease? _lease;
        private bool _attempted;

        public async ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_lease is not null)
                    return _lease;
                _failure?.Throw();
                if (_attempted)
                    throw new InvalidOperationException("Structured Logs persistence startup did not produce a resource lease.");
                _attempted = true;
            }

            IDiagnosticRecordStoreSession? session = null;
            try
            {
                var deployment = deploymentSource.CreateDeploymentManifest();
                session = await sessionFactory.OpenAsync(
                    deployment,
                    new(binding.TenantId, binding.ScopeId),
                    cancellationToken);
                var store = await session.OpenStoreAsync(new(binding.StreamId), cancellationToken);
                storeGate.Publish(store);
                var lease = new StructuredLogResourceLease(storeGate, session);
                lock (_gate)
                    _lease = lease;
                return lease;
            }
            catch (Exception startupFailure)
            {
                storeGate.PublishFailure(startupFailure);
                lock (_gate)
                    _failure = ExceptionDispatchInfo.Capture(startupFailure);
                if (session is not null)
                {
                    try
                    {
                        await session.DisposeAsync();
                    }
                    catch (Exception cleanupFailure)
                    {
                        startupFailure.Data["StructuredLogsStartupCleanupFailure"] = cleanupFailure;
                    }
                }
                ExceptionDispatchInfo.Capture(startupFailure).Throw();
                throw;
            }
        }
    }

    private sealed class StructuredLogResourceLease(
        GroundworkDiagnosticRecordStoreGate storeGate,
        IDiagnosticRecordStoreSession session)
        : IDiagnosticsPersistenceResourceLease
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            storeGate.Release();
            await session.DisposeAsync();
        }
    }

    private sealed class DirectStoreResourceLease : IDiagnosticsPersistenceResourceLease
    {
        public static DirectStoreResourceLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
