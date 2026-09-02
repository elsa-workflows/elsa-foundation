using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>
/// First-party Structured Logs adapter over the public Groundwork v2 storage contracts.
/// ProviderSequence is the durable row identity and is copied into the public entry sequence after
/// commit; caller-provided display sequences are never persisted as ordering state.
/// </summary>
public sealed class GroundworkStructuredLogStore :
    IStructuredLogStore,
    IDiagnosticsPersistenceDrain,
    IDiagnosticsPersistenceStartupResource,
    IAsyncDisposable
{
    private const int DefaultMaxRecentQuerySize = 5_000;
    private const int MaxAppendAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private IStorageSession? session;
    private GroundworkStorageSessionGate? sessionGate;
    private readonly StructuredLogStoreBinding binding;
    private readonly StorageUnit unit;
    private readonly int maxRecentQuerySize;
    private readonly int maxRetainedEntries;
    private readonly IProviderCommandObserver? commandObserver;
    private readonly DiagnosticsDrain<PendingAppend, StructuredLogEntry> drain;
    private readonly V2StartupResource? startupResource;
    private int disposed;

    public GroundworkStructuredLogStore(
        IStorageSession session,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        int maxRetainedEntries = 100_000,
        int retentionInterval = 5_000,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionInterval, 1);
        if (!StringComparer.Ordinal.Equals(session.Unit.Id.Value, StructuredLogsGroundworkStorageSchema.UnitId))
        {
            throw new ArgumentException(
                $"The Groundwork v2 session must be opened for '{StructuredLogsGroundworkStorageSchema.UnitId}'.",
                nameof(session));
        }

        this.session = session;
        sessionGate = null;
        this.binding = binding;
        unit = session.Unit;
        maxRecentQuerySize = Math.Clamp(options.Value.MaxRecentQuerySize, 1, DefaultMaxRecentQuerySize);
        this.maxRetainedEntries = maxRetainedEntries;
        commandObserver = null;
        drain = CreateDrain(options.Value, retentionInterval, observer);
    }

    /// <summary>
    /// Creates the host-composed adapter without opening a provider session. Startup applies and
    /// admits the v2 declaration, then publishes the non-owning session before the drain starts.
    /// </summary>
    public GroundworkStructuredLogStore(
        IStorageProviderConnection connection,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        int maxRetainedEntries = 100_000,
        int retentionInterval = 5_000,
        IDiagnosticsPersistenceObserver? observer = null,
        IProviderCommandObserver? commandObserver = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionInterval, 1);

        session = null;
        sessionGate = new();
        this.binding = binding;
        unit = StructuredLogsGroundworkStorageSchema.CreateUnit();
        maxRecentQuerySize = Math.Clamp(options.Value.MaxRecentQuerySize, 1, DefaultMaxRecentQuerySize);
        this.maxRetainedEntries = maxRetainedEntries;
        this.commandObserver = commandObserver;
        startupResource = new(this, connection);
        drain = CreateDrain(options.Value, retentionInterval, observer);
    }

    public async ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (drain.State == DiagnosticsDrainState.Created)
            throw new InvalidOperationException("The Groundwork structured-log capture drain must be started before use.");
        try
        {
            return await drain.EnqueueAsync(new PendingAppend(entry, Guid.NewGuid().ToString("N")), cancellationToken);
        }
        catch (DiagnosticsDrainException exception)
        {
            throw new StructuredLogsException("The structured log append could not be committed.", exception);
        }
    }

    public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = GetSession().Inspect();
        return Task.FromResult(inspection.LifetimeCommittedSequenceHighWater ?? 0L);
    }

    public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(
        StructuredLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = filter.MaxCount is { } requested
            ? Math.Clamp(requested, 0, maxRecentQuerySize)
            : maxRecentQuerySize;
        if (limit == 0)
            return Task.FromResult<IReadOnlyList<StructuredLogEntry>>([]);

        var page = GetSession().Query(Query(StructuredLogGroundworkQuery.All, limit, descending: true, BuildFilter(filter)));
        var entries = page.Rows.Select(ToEntry).Where(filter.Matches).ToList();
        entries.Reverse();
        return Task.FromResult<IReadOnlyList<StructuredLogEntry>>(entries);
    }

    public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = GetSession().Query(Query(StructuredLogGroundworkQuery.All, 1, descending: true));
        return Task.FromResult<StructuredLogReplayCursor?>(page.Rows.Count == 0 ? null : ToEntry(page.Rows[0]).ReplayCursor);
    }

    public Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = GetSession().Inspect().LifetimeCommittedSequenceHighWater ?? 0L;
        var lower = 0L;
        if (afterCursor is { } cursor)
        {
            var anchor = ValidateAnchor(cursor);
            lower = anchor.Sequence;
        }

        if (snapshot <= lower)
            return Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        var predicates = new List<Predicate>
        {
            new Predicate.Range(
                Columns.Sequence,
                Bound.Exclusive(QueryConstant.Of(Columns.Sequence, lower)),
                Bound.Inclusive(QueryConstant.Of(Columns.Sequence, snapshot)))
        };
        var page = GetSession().Query(Query(StructuredLogGroundworkQuery.All, Math.Min(maxCount, maxRecentQuerySize), false, new Predicate.And(predicates)));
        var scanned = page.Rows.Select(ToEntry).ToArray();
        var next = scanned.Length == 0 ? afterCursor : scanned[^1].ReplayCursor;
        return Task.FromResult(new StructuredLogReadPage(scanned.Where(filter.Matches).ToArray(), next, page.NextContinuationToken is not null));
    }

    public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyRetention(keepNewest, cancellationToken);
        return Task.CompletedTask;
    }

    public void Start() => drain.Start();

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref disposed, 1);
        await drain.StopIfStartedAsync(cancellationToken);
    }

    ValueTask<IDiagnosticsPersistenceResourceLease> IDiagnosticsPersistenceStartupResource.AcquireAsync(
        CancellationToken cancellationToken) =>
        startupResource?.AcquireAsync(cancellationToken)
        ?? ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(DirectResourceLease.Instance);

    public ValueTask DisposeAsync() => new(StopAsync());

    private StructuredLogEntry ToEntry(IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue(StructuredLogsGroundworkStorageSchema.PayloadField, out var payload))
            throw new StructuredLogsException("Groundwork returned an invalid structured log payload.");
        var json = payload switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new StructuredLogsException("Groundwork returned an invalid structured log payload.")
        };
        var entry = JsonSerializer.Deserialize<StructuredLogEntry>(json, SerializerOptions)
                    ?? throw new StructuredLogsException("Groundwork returned an invalid structured log payload.");
        if (!row.TryGetValue(StructuredLogsGroundworkStorageSchema.SequenceField, out var sequenceValue) || sequenceValue is null)
            throw new StructuredLogsException("Groundwork returned a structured log row without its provider sequence.");
        var sequence = Convert.ToInt64(sequenceValue, System.Globalization.CultureInfo.InvariantCulture);
        var recordToken = row.GetValueOrDefault(StructuredLogsGroundworkStorageSchema.ReplayTokenField)?.ToString();
        if (string.IsNullOrWhiteSpace(recordToken))
            throw new StructuredLogsException("Groundwork returned a structured log row without its replay token.");
        return entry with
        {
            Sequence = sequence,
            ReplayCursor = GroundworkReplayCursorCodec.Encode(
                binding,
                entry.SourceId,
                recordToken,
                sequence.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    private Anchor ValidateAnchor(StructuredLogReplayCursor cursor)
    {
        if (!GroundworkReplayCursorCodec.TryDecode(cursor, binding, out var parts))
            throw new StructuredLogReplayCursorUnavailableException();
        var tokenColumn = Columns.ReplayToken;
        var page = GetSession().Query(Query(
            StructuredLogGroundworkQuery.Anchor,
            2,
            descending: false,
            new Predicate.Equal(tokenColumn, QueryConstant.Of(tokenColumn, parts.RecordToken))));
        if (page.Rows.Count != 1)
            throw new StructuredLogReplayCursorUnavailableException();
        var row = page.Rows[0];
        var entry = ToEntry(row);
        if (!StringComparer.Ordinal.Equals(entry.SourceId, parts.EntrySourceId) ||
            !StringComparer.Ordinal.Equals(entry.ReplayCursor?.Value, cursor.Value))
            throw new StructuredLogReplayCursorUnavailableException();
        return new(Convert.ToInt64(row[StructuredLogsGroundworkStorageSchema.SequenceField], System.Globalization.CultureInfo.InvariantCulture));
    }

    private QueryRequest Query(
        StructuredLogGroundworkQuery query,
        int limit,
        bool descending,
        Predicate? predicate = null)
    {
        var order = ImmutableArray.Create(new OrderTerm(
            Columns.Sequence,
            descending ? OrderDirection.Descending : OrderDirection.Ascending,
            descending ? NullOrder.First : NullOrder.Last));
        return new(
            new TableId(unit.Name),
            predicate ?? new Predicate.AlwaysTrue(),
            order,
            Projection.All,
            Paging.Keyset(limit));
    }

    private Predicate? BuildFilter(StructuredLogFilter filter)
    {
        var predicates = new List<Predicate>();
        if (filter.MinimumLevel is { } minimum)
        {
            predicates.Add(new Predicate.Range(
                Columns.Level,
                Bound.Inclusive(QueryConstant.Of(Columns.Level, (long)minimum)),
                Bound.Inclusive(QueryConstant.Of(Columns.Level, (long)LogLevel.None))));
        }
        if (!string.IsNullOrEmpty(filter.Category))
            predicates.Add(new Predicate.Equal(
                Columns.CategoryKey,
                QueryConstant.Of(Columns.CategoryKey, Hash(filter.Category))));
        if (!string.IsNullOrEmpty(filter.SourceId))
            predicates.Add(new Predicate.Equal(
                Columns.SourceKey,
                QueryConstant.Of(Columns.SourceKey, Hash(filter.SourceId))));
        return predicates.Count switch
        {
            0 => null,
            1 => predicates[0],
            _ => new Predicate.And(predicates)
        };
    }

    private IStorageSession GetSession() =>
        sessionGate?.Current ?? session ?? throw new InvalidOperationException(
            "The Groundwork v2 storage session has not completed startup admission.");

    private void PublishSession(IStorageSession value)
    {
        if (sessionGate is { } gate)
            gate.Publish(value);
        else
            session = value;
    }

    private void PublishFailure(Exception exception)
    {
        if (sessionGate is { } gate)
            gate.PublishFailure(exception);
    }

    private void ReleaseSession()
    {
        if (sessionGate is { } gate)
            gate.Release();
        else
            session = null;
    }

    private DiagnosticsDrain<PendingAppend, StructuredLogEntry> CreateDrain(
        StructuredLogsOptions options,
        int retentionInterval,
        IDiagnosticsPersistenceObserver? observer) =>
        new(
            new V2DrainTarget(this),
            new DiagnosticsDrainOptions
            {
                BatchSize = 200,
                QueueCapacity = Math.Max(options.BufferCapacity, 200) * 4,
                RetentionInterval = retentionInterval,
                MaxAttempts = MaxAppendAttempts,
                BaseRetryDelay = RetryDelay,
                MaxRetryDelay = TimeSpan.FromSeconds(5),
                ShutdownTimeout = options.ShutdownDrainTimeout <= TimeSpan.Zero
                    ? TimeSpan.FromTicks(1)
                    : options.ShutdownDrainTimeout
            },
            observer);

    private async ValueTask<DiagnosticsDrainCommit<StructuredLogEntry>> CommitBatchAsync(
        DiagnosticsDrainBatch<PendingAppend> batch,
        CancellationToken cancellationToken)
    {
        var values = batch.Items.Select(ToValues).ToArray();
        var operation = new OperationId(batch.Id.IssuedAt, $"structured_logs_v2_{batch.Id}");
        AppendOutcomeReport? report = null;
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaxAppendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                report = GetSession().AppendWithOutcomes(operation, values);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (attempt < MaxAppendAttempts)
            {
                lastFailure = exception;
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        if (report is null)
            throw new StructuredLogsException("The structured log append could not be committed.", lastFailure!);
        if (report.Outcomes.Count != batch.Items.Count)
            throw new StructuredLogsException("Groundwork returned an incomplete structured log append result.");

        var committed = batch.Items.Select((item, index) =>
        {
            var sequence = report.Outcomes[index].GeneratedValue<long>(StructuredLogsGroundworkStorageSchema.SequenceField);
            return item.Entry with
            {
                Sequence = sequence,
                ReplayCursor = GroundworkReplayCursorCodec.Encode(
                    binding,
                    item.Entry.SourceId,
                    item.RecordToken,
                    sequence.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
        }).ToArray();
        return new DiagnosticsDrainCommit<StructuredLogEntry>(committed, committed.Length);
    }

    private StorageValues ToValues(PendingAppend pending) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [StructuredLogsGroundworkStorageSchema.TimestampField] = pending.Entry.Timestamp,
        [StructuredLogsGroundworkStorageSchema.LevelField] = (long)pending.Entry.Level,
        [StructuredLogsGroundworkStorageSchema.CategoryKeyField] = Hash(pending.Entry.Category),
        [StructuredLogsGroundworkStorageSchema.SourceKeyField] = Hash(pending.Entry.SourceId),
        [StructuredLogsGroundworkStorageSchema.ReplayTokenField] = pending.RecordToken,
        [StructuredLogsGroundworkStorageSchema.PayloadField] = JsonSerializer.Serialize(
            pending.Entry with { Sequence = 0, ReplayCursor = null }, SerializerOptions)
    });

    private int ApplyRetention(
        int keepNewest,
        CancellationToken cancellationToken,
        OperationId? operation = null)
    {
        var operationId = operation ?? new OperationId(
            DateTimeOffset.UtcNow,
            $"structured_logs_v2_retention_{Guid.NewGuid():N}");
        var result = GetSession().ApplyRetention(
            operationId,
            new RetentionExecutionOptions
            {
                KeepNewestOverride = keepNewest,
                CancellationToken = cancellationToken
            });
        return result.DeletedRows;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ValidateBinding(StructuredLogStoreBinding value)
    {
        foreach (var part in new[] { value.TenantId, value.ScopeId, value.StreamId })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(part);
            if (part.Length > 64 || part.Any(character => character is < '!' or > '~'))
                throw new ArgumentException(
                    "Structured log binding values must use printable ASCII and be bounded to 64 code units.",
                    nameof(value));
        }
        try
        {
            _ = StructuredLogsGroundworkStorageSchema.ScopeFor(value);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "The combined structured log tenant, scope, and stream identity exceeds the Groundwork v2 scope limit.",
                nameof(value),
                exception);
        }
    }

    private enum StructuredLogGroundworkQuery
    {
        All,
        Anchor
    }

    private static class Columns
    {
        internal static ColumnRef Sequence { get; } = Column(StructuredLogsGroundworkStorageSchema.SequenceField, QueryType.Int64, false);
        internal static ColumnRef Level { get; } = Column(StructuredLogsGroundworkStorageSchema.LevelField, QueryType.Int64, false);
        internal static ColumnRef CategoryKey { get; } = Column(StructuredLogsGroundworkStorageSchema.CategoryKeyField, QueryType.String, false, 128);
        internal static ColumnRef SourceKey { get; } = Column(StructuredLogsGroundworkStorageSchema.SourceKeyField, QueryType.String, false, 128);
        internal static ColumnRef ReplayToken { get; } = Column(StructuredLogsGroundworkStorageSchema.ReplayTokenField, QueryType.String, false, 64);

        private static ColumnRef Column(string name, QueryType type, bool nullable, int? maxLength = null) =>
            new(new TableId(StructuredLogsGroundworkStorageSchema.UnitName), name, type, nullable, maxLength);
    }

    private sealed record Anchor(long Sequence);

    private sealed record PendingAppend(StructuredLogEntry Entry, string RecordToken);

    private sealed class V2DrainTarget(GroundworkStructuredLogStore owner) : IDiagnosticsDrainTarget<PendingAppend, StructuredLogEntry>
    {
        private readonly Lock retentionGate = new();
        private OperationId? retentionOperation;

        public ValueTask<DiagnosticsDrainCommit<StructuredLogEntry>> CommitAsync(
            DiagnosticsDrainBatch<PendingAppend> batch,
            CancellationToken cancellationToken = default) =>
            owner.CommitBatchAsync(batch, cancellationToken);

        public ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            OperationId operation;
            lock (retentionGate)
            {
                operation = retentionOperation ??= new OperationId(
                    DateTimeOffset.UtcNow,
                    $"structured_logs_v2_retention_{Guid.NewGuid():N}");
            }

            try
            {
                var deleted = owner.ApplyRetention(owner.maxRetainedEntries, cancellationToken, operation);
                lock (retentionGate)
                {
                    if (retentionOperation == operation)
                        retentionOperation = null;
                }
                return ValueTask.FromResult(deleted);
            }
            catch
            {
                // Keep the operation identity until the provider acknowledges the exact result.
                // DiagnosticsDrain may call us again after an acknowledgement-loss or transient
                // failure; a new nonce could repeat a partially observed retention pass.
                throw;
            }
        }
    }

    private sealed class V2StartupResource(
        GroundworkStructuredLogStore owner,
        IStorageProviderConnection connection)
    {
        private readonly Lock gate = new();
        private ExceptionDispatchInfo? failure;
        private V2ResourceLease? lease;
        private bool attempted;

        public ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (lease is not null)
                    return ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(lease);
                failure?.Throw();
                if (attempted)
                    throw new InvalidOperationException("Groundwork v2 structured-log startup did not produce a resource lease.");
                attempted = true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Schema.Apply(owner.unit);
                var opened = connection.OpenSession(
                    owner.unit,
                    StorageAccess.Scoped(StructuredLogsGroundworkStorageSchema.ScopeFor(owner.binding)),
                    owner.commandObserver);
                owner.PublishSession(opened);
                var created = new V2ResourceLease(owner);
                lock (gate)
                    lease = created;
                return ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(created);
            }
            catch (Exception exception)
            {
                owner.ReleaseSession();
                owner.PublishFailure(exception);
                lock (gate)
                    failure = ExceptionDispatchInfo.Capture(exception);
                ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }
        }
    }

    private sealed class V2ResourceLease(GroundworkStructuredLogStore owner) : IDiagnosticsPersistenceResourceLease
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.ReleaseSession();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DirectResourceLease : IDiagnosticsPersistenceResourceLease
    {
        public static DirectResourceLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
