using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;

public sealed class EfStructuredLogStore : IStructuredLogStore, IDiagnosticsPersistenceDrain, IAsyncDisposable
{
    private const int DefaultMaxRetainedEntries = 100_000;
    private const int DrainBatchSize = 200;
    private const int MaxDrainAttempts = 3;
    private const int RetentionInterval = 5_000;
    private const int RetentionDeleteBatchSize = 1_000;
    private static readonly TimeSpan AppendIdempotencyWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceScopeFactory scopeFactory;
    private readonly StructuredLogStoreBinding binding;
    private readonly int maxRecentQuerySize;
    private readonly int maxRetainedEntries;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly DiagnosticsDrain<PendingAppend, StructuredLogEntry> drain;
    private int disposed;

    public EfStructuredLogStore(
        IServiceScopeFactory scopeFactory,
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding binding,
        IDiagnosticsPersistenceObserver? observer = null,
        int maxRetainedEntries = DefaultMaxRetainedEntries,
        int retentionInterval = RetentionInterval)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionInterval, 1);

        this.scopeFactory = scopeFactory;
        this.binding = binding;
        maxRecentQuerySize = Math.Max(1, options.Value.MaxRecentQuerySize);
        this.maxRetainedEntries = maxRetainedEntries;
        drain = new(
            new DrainTarget(this),
            new DiagnosticsDrainOptions
            {
                BatchSize = DrainBatchSize,
                QueueCapacity = Math.Max(options.Value.BufferCapacity, DrainBatchSize) * 4,
                RetentionInterval = retentionInterval,
                MaxAttempts = MaxDrainAttempts,
                BaseRetryDelay = RetryDelay,
                MaxRetryDelay = TimeSpan.FromSeconds(5),
                ShutdownTimeout = options.Value.ShutdownDrainTimeout <= TimeSpan.Zero
                    ? TimeSpan.FromTicks(1)
                    : options.Value.ShutdownDrainTimeout
            },
            observer);
    }

    public async ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (drain.State == DiagnosticsDrainState.Created)
            throw new InvalidOperationException("The EF structured-log capture drain must be started before use.");

        try
        {
            return await drain.EnqueueAsync(
                new PendingAppend(entry, Guid.NewGuid().ToString("N")),
                cancellationToken);
        }
        catch (DiagnosticsDrainException exception)
        {
            throw new StructuredLogsException("The structured log append could not be committed.", exception);
        }
    }

    public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) =>
        ExecuteReadAsync(async (db, ct) =>
        {
            var state = await db.StreamStates.AsNoTracking()
                .SingleOrDefaultAsync(value => value.ScopeKey == ScopeKey, ct);
            ValidateState(state);
            return state?.HighWater ?? 0L;
        }, cancellationToken);

    public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(
        StructuredLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return ExecuteReadAsync(async (db, ct) =>
        {
            var limit = filter.MaxCount is { } requested
                ? Math.Clamp(requested, 0, maxRecentQuerySize)
                : maxRecentQuerySize;
            if (limit == 0)
                return (IReadOnlyList<StructuredLogEntry>)[];

            var categoryKey = string.IsNullOrEmpty(filter.Category) ? null : Hash(filter.Category);
            var sourceKey = string.IsNullOrEmpty(filter.SourceId) ? null : Hash(filter.SourceId);
            var query = db.Records.AsNoTracking()
                .Where(record => record.ScopeKey == ScopeKey);
            if (filter.MinimumLevel is { } minimum)
                query = query.Where(record => record.Level >= (int)minimum);
            if (categoryKey is not null)
                query = query.Where(record => record.CategoryKey == categoryKey);
            if (sourceKey is not null)
                query = query.Where(record => record.SourceKey == sourceKey);

            var rows = await query
                .OrderByDescending(record => record.Position)
                .Take(limit)
                .ToListAsync(ct);
            ValidateRecords(rows);
            var result = rows
                .Select(ToEntry)
                .Where(filter.Matches)
                .Reverse()
                .ToArray();
            return (IReadOnlyList<StructuredLogEntry>)result;
        }, cancellationToken);
    }

    public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
        ExecuteReadAsync(async (db, ct) =>
        {
            var row = await db.Records.AsNoTracking()
                .Where(record => record.ScopeKey == ScopeKey)
                .OrderByDescending(record => record.Position)
                .FirstOrDefaultAsync(ct);
            if (row is null)
                return null;
            ValidateRecord(row);
            return ToEntry(row).ReplayCursor;
        }, cancellationToken);

    public Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        return ExecuteReadAsync(async (db, ct) =>
        {
            var limit = Math.Min(maxCount, maxRecentQuerySize);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var state = await db.StreamStates.AsNoTracking()
                .SingleOrDefaultAsync(value => value.ScopeKey == ScopeKey, ct);
            ValidateState(state);
            var snapshot = state?.HighWater ?? 0L;
            var lower = 0L;
            if (afterCursor is { } cursor)
            {
                var anchor = await ValidateAnchorAsync(db, cursor, ct);
                lower = anchor.Position;
            }

            if (snapshot <= lower)
            {
                await transaction.CommitAsync(ct);
                return new StructuredLogReadPage([], afterCursor, false);
            }

            var rows = await db.Records.AsNoTracking()
                .Where(record => record.ScopeKey == ScopeKey &&
                                 record.Position > lower &&
                                 record.Position <= snapshot)
                .OrderBy(record => record.Position)
                .Take(limit)
                .ToListAsync(ct);
            ValidateRecords(rows);
            var lastPosition = rows.Count == 0 ? 0 : rows[rows.Count - 1].Position;
            var next = rows.Count == 0 ? afterCursor : ToEntry(rows[rows.Count - 1]).ReplayCursor;
            var hasMore = rows.Count > 0 && await db.Records.AsNoTracking().AnyAsync(
                record => record.ScopeKey == ScopeKey && record.Position > lastPosition && record.Position <= snapshot,
                ct);
            await transaction.CommitAsync(ct);
            return new StructuredLogReadPage(rows.Select(ToEntry).Where(filter.Matches).ToArray(), next, hasMore);
        }, cancellationToken);
    }

    public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        return ExecuteWriteAsync((db, ct) => TrimCoreAsync(db, keepNewest, ct), cancellationToken);
    }

    public void Start() => drain.Start();

    public Task ApplyPendingRetentionAsync(CancellationToken cancellationToken = default) =>
        drain.ApplyPendingRetentionAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref disposed, 1);
        await drain.StopIfStartedAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            operationGate.Dispose();
        }
    }

    private async ValueTask<DiagnosticsDrainCommit<StructuredLogEntry>> CommitBatchAsync(
        DiagnosticsDrainBatch<PendingAppend> batch,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var pending = batch.Items
                .Select(item => new EfPendingAppend(item.RecordToken, SerializePayload(item.Entry)))
                .ToArray();
            var fingerprint = StructuredLogAppendFingerprint.Compute(binding, pending);
            var batchId = batch.Id.Value.ToString("N");
            var state = await db.StreamStates
                .SingleOrDefaultAsync(value => value.ScopeKey == ScopeKey, cancellationToken);
            ValidateState(state);
            var stateWasCreated = state is null;
            state ??= new StructuredLogStreamState
            {
                ScopeKey = ScopeKey,
                TenantId = binding.TenantId,
                ScopeId = binding.ScopeId,
                StreamId = binding.StreamId,
                HighWater = 0,
                Version = NewVersion()
            };
            var now = DateTimeOffset.UtcNow;
            var cutoffAdvanced = AdvanceAppendOperationCutoff(state, now);
            if (stateWasCreated)
                db.StreamStates.Add(state);
            if (cutoffAdvanced)
            {
                state.Version = NewVersion();
                state.UpdatedAtTicks = now.UtcTicks;
            }
            await PruneAppendOperationsAsync(db, state.AppendOperationCutoffTicks, cancellationToken);

            if (batch.Id.IssuedAt.UtcTicks <= state.AppendOperationCutoffTicks)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new StructuredLogsException("The structured log append operation has expired.");
            }

            var existingOperation = await db.AppendOperations
                .SingleOrDefaultAsync(operation => operation.ScopeKey == ScopeKey && operation.BatchId == batchId, cancellationToken);
            if (existingOperation is not null)
            {
                ValidateBinding(existingOperation.TenantId, existingOperation.ScopeId, existingOperation.StreamId);
                if (existingOperation.IssuedAtTicks != batch.Id.IssuedAt.UtcTicks)
                    throw new StructuredLogsException("The structured log append operation identity was reused with a different issue time.");
                if (!StringComparer.Ordinal.Equals(existingOperation.Fingerprint, fingerprint))
                    throw new StructuredLogsException("The structured log append operation was reused with a different payload.");

                var replayed = DeserializeOutcome(existingOperation.OutcomeJson);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new DiagnosticsDrainCommit<StructuredLogEntry>(
                    replayed.Select(ToEntry).ToArray(),
                    replayed.Length);
            }

            var firstPosition = checked(state.HighWater + 1);
            var outcomes = pending.Select((item, index) => new PersistedOutcome(
                checked(firstPosition + index),
                item.RecordToken,
                item.PayloadJson)).ToArray();
            foreach (var outcome in outcomes)
            {
                var entry = DeserializePayload(outcome.PayloadJson);
                db.Records.Add(new StructuredLogRecord
                {
                    ScopeKey = ScopeKey,
                    Position = outcome.Position,
                    TenantId = binding.TenantId,
                    ScopeId = binding.ScopeId,
                    StreamId = binding.StreamId,
                    TimestampTicks = entry.Timestamp.Ticks,
                    TimestampOffsetMinutes = checked((short)entry.Timestamp.Offset.TotalMinutes),
                    Level = (int)entry.Level,
                    CategoryKey = Hash(entry.Category),
                    SourceKey = Hash(entry.SourceId),
                    ReplayToken = outcome.RecordToken,
                    PayloadJson = outcome.PayloadJson
                });
            }

            state.HighWater = checked(firstPosition + pending.Length - 1);
            state.Version = NewVersion();
            state.UpdatedAtTicks = now.UtcTicks;

            db.AppendOperations.Add(new StructuredLogAppendOperation
            {
                ScopeKey = ScopeKey,
                BatchId = batchId,
                TenantId = binding.TenantId,
                ScopeId = binding.ScopeId,
                StreamId = binding.StreamId,
                IssuedAtTicks = batch.Id.IssuedAt.UtcTicks,
                Fingerprint = fingerprint,
                OutcomeJson = JsonSerializer.Serialize(outcomes, SerializerOptions)
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DiagnosticsDrainCommit<StructuredLogEntry>(outcomes.Select(ToEntry).ToArray(), outcomes.Length);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        return await ExecuteWriteAsync((db, ct) => TrimCoreAsync(db, maxRetainedEntries, ct), cancellationToken);
    }

    private async Task<int> TrimCoreAsync(StructuredLogsDbContext db, int keepNewest, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var state = await db.StreamStates.SingleOrDefaultAsync(value => value.ScopeKey == ScopeKey, cancellationToken);
        ValidateState(state);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        AdvanceAppendOperationCutoff(state, now);
        await PruneAppendOperationsAsync(db, state.AppendOperationCutoffTicks, cancellationToken);

        StructuredLogRecord? boundaryRecord = null;
        if (keepNewest > 0)
        {
            boundaryRecord = await db.Records
                .Where(record => record.ScopeKey == ScopeKey)
                .OrderByDescending(record => record.Position)
                .Skip(keepNewest - 1)
                .FirstOrDefaultAsync(cancellationToken);
            if (boundaryRecord is not null)
                ValidateRecord(boundaryRecord);
        }

        var deletedCount = 0;
        while (true)
        {
            var deleteBatch = await db.Records
                .Where(record => record.ScopeKey == ScopeKey &&
                                 (keepNewest == 0 || boundaryRecord != null && record.Position < boundaryRecord.Position))
                .OrderBy(record => record.Position)
                .Take(RetentionDeleteBatchSize)
                .ToListAsync(cancellationToken);
            if (deleteBatch.Count == 0)
                break;
            ValidateRecords(deleteBatch);
            db.Records.RemoveRange(deleteBatch);
            deletedCount += deleteBatch.Count;
            await db.SaveChangesAsync(cancellationToken);
        }
        state.Version = NewVersion();
        state.UpdatedAtTicks = now.UtcTicks;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deletedCount;
    }

    private async Task PruneAppendOperationsAsync(
        StructuredLogsDbContext db,
        long cutoffTicks,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var expired = await db.AppendOperations
                .Where(operation => operation.ScopeKey == ScopeKey && operation.IssuedAtTicks <= cutoffTicks)
                .OrderBy(operation => operation.IssuedAtTicks)
                .Take(RetentionDeleteBatchSize)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0)
                return;
            foreach (var operation in expired)
                ValidateBinding(operation.TenantId, operation.ScopeId, operation.StreamId);
            db.AppendOperations.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool AdvanceAppendOperationCutoff(StructuredLogStreamState state, DateTimeOffset now)
    {
        var observedCutoff = now.Subtract(AppendIdempotencyWindow).UtcTicks;
        if (observedCutoff <= state.AppendOperationCutoffTicks)
            return false;
        state.AppendOperationCutoffTicks = observedCutoff;
        return true;
    }

    private async Task<StructuredLogRecord> ValidateAnchorAsync(
        StructuredLogsDbContext db,
        StructuredLogReplayCursor cursor,
        CancellationToken cancellationToken)
    {
        if (!EfReplayCursorCodec.TryDecode(cursor, binding, out var parts))
            throw new StructuredLogReplayCursorUnavailableException();
        var row = await db.Records.AsNoTracking()
            .SingleOrDefaultAsync(record => record.ScopeKey == ScopeKey && record.Position == parts.Position, cancellationToken);
        if (row is null)
            throw new StructuredLogReplayCursorUnavailableException();
        ValidateRecord(row);
        var entry = ToEntry(row);
        if (entry.ReplayCursor is not { } entryReplayCursor ||
            !StringComparer.Ordinal.Equals(entry.SourceId, parts.EntrySourceId) ||
            !StringComparer.Ordinal.Equals(row.ReplayToken, parts.ReplayToken) ||
            !StringComparer.Ordinal.Equals(entryReplayCursor.Value, cursor.Value))
            throw new StructuredLogReplayCursorUnavailableException();
        return row;
    }

    private Task<T> ExecuteReadAsync<T>(
        Func<StructuredLogsDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteScopedAsync(operation, "The EF structured-log read operation failed.", cancellationToken);

    private Task<T> ExecuteWriteAsync<T>(
        Func<StructuredLogsDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteScopedAsync(operation, "The EF structured-log write operation failed.", cancellationToken);

    private async Task<T> ExecuteScopedAsync<T>(
        Func<StructuredLogsDbContext, CancellationToken, Task<T>> operation,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
                return await operation(db, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StructuredLogsException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new StructuredLogsException(failureMessage, exception);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private string ScopeKey => EfReplayCursorCodec.BindingHash(binding);

    private StructuredLogEntry ToEntry(StructuredLogRecord row)
    {
        var payload = DeserializePayload(row.PayloadJson);
        return payload with
        {
            Timestamp = new DateTimeOffset(row.TimestampTicks, TimeSpan.FromMinutes(row.TimestampOffsetMinutes)),
            Sequence = row.Position,
            ReplayCursor = EfReplayCursorCodec.Encode(binding, payload.SourceId, row.ReplayToken, row.Position)
        };
    }

    private StructuredLogEntry ToEntry(PersistedOutcome outcome)
    {
        var payload = DeserializePayload(outcome.PayloadJson);
        return payload with
        {
            Sequence = outcome.Position,
            ReplayCursor = EfReplayCursorCodec.Encode(binding, payload.SourceId, outcome.RecordToken, outcome.Position)
        };
    }

    private PersistedOutcome[] DeserializeOutcome(string json) =>
        JsonSerializer.Deserialize<PersistedOutcome[]>(json, SerializerOptions)
        ?? throw new StructuredLogsException("The structured log append ledger contains an invalid outcome.");

    private StructuredLogEntry DeserializePayload(string json) =>
        JsonSerializer.Deserialize<StructuredLogEntry>(json, SerializerOptions)
        ?? throw new StructuredLogsException("The EF structured log payload is invalid.");

    private static string SerializePayload(StructuredLogEntry entry) =>
        JsonSerializer.Serialize(entry with { Sequence = 0, ReplayCursor = null }, SerializerOptions);

    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private void ValidateRecords(IEnumerable<StructuredLogRecord> rows)
    {
        foreach (var row in rows)
            ValidateRecord(row);
    }

    private void ValidateRecord(StructuredLogRecord row) =>
        ValidateBinding(row.TenantId, row.ScopeId, row.StreamId);

    private void ValidateState(StructuredLogStreamState? state)
    {
        if (state is not null)
            ValidateBinding(state.TenantId, state.ScopeId, state.StreamId);
    }

    private static void ValidateBinding(StructuredLogStoreBinding value)
    {
        foreach (var part in new[] { value.TenantId, value.ScopeId, value.StreamId })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(part);
            if (part.Length > 64 || part.Any(character => character is < '!' or > '~'))
                throw new ArgumentException("Structured log binding values must use printable ASCII and be bounded to 64 code units.", nameof(value));
        }
    }

    private void ValidateBinding(string tenantId, string scopeId, string streamId)
    {
        if (!StringComparer.Ordinal.Equals(tenantId, binding.TenantId) ||
            !StringComparer.Ordinal.Equals(scopeId, binding.ScopeId) ||
            !StringComparer.Ordinal.Equals(streamId, binding.StreamId))
            throw new StructuredLogsException("The EF structured-log scope binding is inconsistent.");
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N");

    private sealed record PendingAppend(StructuredLogEntry Entry, string RecordToken);
    private sealed record PersistedOutcome(long Position, string RecordToken, string PayloadJson);

    private sealed class DrainTarget(EfStructuredLogStore owner) : IDiagnosticsDrainTarget<PendingAppend, StructuredLogEntry>
    {
        public ValueTask<DiagnosticsDrainCommit<StructuredLogEntry>> CommitAsync(
            DiagnosticsDrainBatch<PendingAppend> batch,
            CancellationToken cancellationToken = default) => owner.CommitBatchAsync(batch, cancellationToken);

        public ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default) =>
            owner.ApplyRetentionAsync(cancellationToken);
    }
}
