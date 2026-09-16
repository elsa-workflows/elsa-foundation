using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Atomically reconciles detached FireAndForget dispatches when an EF test scope closes (R13).</summary>
/// <remarks>
/// Selection is deliberately bounded and provider-neutral. The final scope CAS, dispatch transitions, and
/// cancellation outbox upserts are staged on the caller's shared context and committed as one transaction.
/// </remarks>
public sealed class EfWorkflowTestScopeCleanupStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowTestScopeCleanupStore
{
    private const string CursorPurpose = "ef-runtime-test-scope-cleanup-v1";
    // A dispatch identity is currently short, but the contract accepts 450 UTF-16 code units. A signed cursor for
    // the full UTF-8 representation plus framing and HMAC must remain representable for every accepted identity.
    private const int MaximumContinuationTokenLength = 4096;
    private const byte ContinuationTokenVersion = 1;
    private const int ScopeBindingLength = 32;
    private const int ContinuationHeaderLength = 1 + ScopeBindingLength + sizeof(long);
    private const int ProviderPageSize = WorkflowDispatchQuery.MaximumTake;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly BookmarkStateDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor _access = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
    private readonly IRuntimeRecoveryContinuationCodec _codec = continuationCodec ?? throw new ArgumentNullException(nameof(continuationCodec));

    public async ValueTask<WorkflowTestScopeCleanupResult> CleanupAsync(
        WorkflowTestScope scope,
        DateTimeOffset requestedAt,
        int pageSize,
        IReadOnlyDictionary<string, RuntimePostCommitIntent> cancellationIntents,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(cancellationIntents);
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));
        if (pageSize is <= 0 or > RuntimeWorkflowTestScopeEfModule.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var continuation = DecodeContinuation(continuationToken, scope);
        cancellationToken.ThrowIfCancellationRequested();
        var accessScope = RequireScope();
        _access.Current.EnsureTenantScope(scope.TenantId);
        await EnsureClosingAsync(scope, accessScope, cancellationToken);

        var candidates = await QueryActionableAsync(scope, accessScope, continuation, pageSize + 1, cancellationToken);
        var page = candidates.Take(pageSize).ToArray();
        var inspected = page.Length;
        var cancelledBeforeAdmission = 0;
        var cancellationQueued = 0;
        var terminalUnchanged = 0;

        if (page.Length > 0)
        {
            var touched = new List<object>(page.Length + 2);
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var scopeRow = await LoadScopeAsync(accessScope, scope.ScopeId, tracking: true, cancellationToken)
                                ?? throw new InvalidOperationException("The workflow test scope was not found for cleanup.");
                var scopeRecord = WorkflowTestScopeEfSupport.Read(scopeRow, accessScope, scope.ScopeId);
                EnsureClosing(scopeRecord, scope);
                touched.Add(scopeRow);

                // The revision-only write is the EF equivalent of a same-value conditional upsert.
                // It fences cleanup against a concurrent admission or closure without clearing caller-owned state.
                WorkflowTestScopeEfSupport.StageAdmission(scopeRow);

                foreach (var candidate in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dispatchRow = await LoadDispatchAsync(accessScope, candidate.DispatchId, tracking: true, cancellationToken);
                    if (dispatchRow is null)
                    {
                        terminalUnchanged++;
                        continue;
                    }

                    touched.Add(dispatchRow);
                    var current = WorkflowDispatchEfSupport.ReadChecked(dispatchRow, accessScope, candidate.DispatchId);
                    _access.Current.EnsureTenantScope(current.TenantId);
                    if (!WorkflowTestScope.ContextEquals(current.TestScope, scope) ||
                        current.Mode != WorkflowDispatchMode.FireAndForget)
                    {
                        throw new InvalidDataException(
                            $"Workflow dispatch '{current.DispatchId}' changed immutable test-scope cleanup context.");
                    }

                    if (current.Status == WorkflowDispatchStatus.Pending)
                    {
                        var cancelled = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(current, requestedAt);
                        WorkflowDispatchEfSupport.Copy(dispatchRow, cancelled, accessScope, checked(dispatchRow.Revision + 1));
                        cancelledBeforeAdmission++;
                        continue;
                    }

                    if (current.Status != WorkflowDispatchStatus.Started)
                    {
                        terminalUnchanged++;
                        continue;
                    }

                    if (!cancellationIntents.TryGetValue(current.DispatchId, out var intent))
                    {
                        throw new InvalidOperationException(
                            "A started test-scope dispatch requires deterministic cancellation responsibility.");
                    }

                    WorkflowDispatchLifecycle.ValidateTestScopeCancellationIntent(current, intent);
                    var marked = WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(current)
                        ? current
                        : WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(current, requestedAt);
                    if (!WorkflowDispatchLifecycle.RecordsEqual(current, marked))
                        WorkflowDispatchEfSupport.Copy(dispatchRow, marked, accessScope, checked(dispatchRow.Revision + 1));

                    var identity = new WorkflowDispatchIdentity(
                        current.ParentWorkflowExecutionId,
                        current.ParentActivityExecutionId);
                    var outboxItem = new RuntimePostCommitOutboxItem(
                        identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}"),
                        intent,
                        RuntimePostCommitOutboxStatus.Pending,
                        requestedAt,
                        requestedAt,
                        RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
                    var outboxRow = await LoadOutboxAsync(accessScope, outboxItem.OutboxItemId, tracking: true, cancellationToken);
                    if (outboxRow is null)
                    {
                        outboxRow = EfRuntimePostCommitOutboxStore.ToEntity(
                            outboxItem,
                            accessScope,
                            EfRuntimePostCommitOutboxStore.RowId(accessScope, outboxItem.OutboxItemId),
                            revision: 1);
                        _context.RuntimePostCommitOutbox.Add(outboxRow);
                        touched.Add(outboxRow);
                    }
                    else
                    {
                        var existing = EfRuntimePostCommitOutboxStore.ReadChecked(
                            outboxRow,
                            accessScope,
                            outboxItem.OutboxItemId);
                        if (!EfRuntimePostCommitOutboxStore.PendingItemsEquivalent(existing, outboxItem))
                        {
                            throw new InvalidOperationException(
                                "The workflow test-scope cancellation outbox item conflicts with committed responsibility.");
                        }
                    }

                    cancellationQueued++;
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await RollbackAndDetachAsync(transaction, touched);
                throw;
            }
        }

        var remainingLive = await CountLiveAsync(scope, accessScope, cancellationToken);
        var next = candidates.Count > pageSize && page.Length > 0
            ? EncodeContinuation(scope, page[^1].CreatedAt, page[^1].DispatchId)
            : null;
        return new WorkflowTestScopeCleanupResult(
            inspected,
            cancelledBeforeAdmission,
            cancellationQueued,
            terminalUnchanged,
            remainingLive,
            next);
    }

    private async ValueTask<IReadOnlyList<WorkflowDispatchRecord>> QueryActionableAsync(
        WorkflowTestScope scope,
        string accessScope,
        DispatchContinuation? continuation,
        int take,
        CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            var afterCreatedAt = continuation?.CreatedAt;
            var afterDispatchId = continuation?.DispatchId;
            var accepted = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = await QueryDispatchPageAsync(
                    scope,
                    accessScope,
                    status,
                    afterCreatedAt,
                    afterDispatchId,
                    cancellationToken);
                foreach (var row in rows)
                {
                    var record = WorkflowDispatchEfSupport.ReadChecked(row, accessScope);
                    _access.Current.EnsureTenantScope(record.TenantId);
                    if (record.Mode == WorkflowDispatchMode.FireAndForget &&
                        (record.Status != WorkflowDispatchStatus.Started ||
                         !WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record)) &&
                        WorkflowTestScope.ContextEquals(record.TestScope, scope))
                    {
                        records[record.DispatchId] = record;
                        accepted++;
                    }
                }

                if (accepted >= take || rows.Length < ProviderPageSize)
                    break;
                var last = rows[^1];
                afterCreatedAt = new DateTimeOffset(last.CreatedAtUtcTicks, TimeSpan.Zero);
                afterDispatchId = EfRelationalIdentity.Decode(last.DispatchId);
            }
        }

        return records.Values
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
    }

    private async ValueTask<int> CountLiveAsync(
        WorkflowTestScope scope,
        string accessScope,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            DateTimeOffset? afterCreatedAt = null;
            string? afterDispatchId = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = await QueryDispatchPageAsync(
                    scope,
                    accessScope,
                    status,
                    afterCreatedAt,
                    afterDispatchId,
                    cancellationToken);
                foreach (var row in rows)
                {
                    var record = WorkflowDispatchEfSupport.ReadChecked(row, accessScope);
                    _access.Current.EnsureTenantScope(record.TenantId);
                    if (record.Mode == WorkflowDispatchMode.FireAndForget &&
                        WorkflowTestScope.ContextEquals(record.TestScope, scope))
                        count++;
                }

                if (rows.Length < ProviderPageSize)
                    break;
                var last = rows[^1];
                afterCreatedAt = new DateTimeOffset(last.CreatedAtUtcTicks, TimeSpan.Zero);
                afterDispatchId = EfRelationalIdentity.Decode(last.DispatchId);
            }
        }

        return count;
    }

    private async ValueTask<WorkflowDispatchEntity[]> QueryDispatchPageAsync(
        WorkflowTestScope scope,
        string accessScope,
        WorkflowDispatchStatus status,
        DateTimeOffset? afterCreatedAt,
        string? afterDispatchId,
        CancellationToken cancellationToken)
    {
        var scopeKey = EfRelationalIdentity.Encode(accessScope);
        var scopeHash = EfRelationalIdentity.Hash(accessScope);
        var testScopeId = EfRelationalIdentity.Encode(scope.ScopeId);
        var testScopeHash = EfRelationalIdentity.Hash(scope.ScopeId);
        var source = _context.WorkflowDispatches.AsNoTracking().Where(row =>
            row.ScopeKey == scopeKey &&
            row.ScopeKeyHash == scopeHash &&
            row.TestScopeId == testScopeId &&
            row.TestScopeIdHash == testScopeHash &&
            row.Mode == (int)WorkflowDispatchMode.FireAndForget &&
            row.Status == (int)status);
        if (afterCreatedAt is { } after)
        {
            var orderKey = WorkflowDispatchEfSupport.DispatchOrderKey(afterDispatchId!);
            source = source.Where(row =>
                row.CreatedAtUtcTicks > after.UtcTicks ||
                row.CreatedAtUtcTicks == after.UtcTicks && string.Compare(row.DispatchIdOrderKey, orderKey) > 0);
        }

        return await source
            .OrderBy(row => row.CreatedAtUtcTicks)
            .ThenBy(row => row.DispatchIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(ProviderPageSize)
            .ToArrayAsync(cancellationToken);
    }

    private async ValueTask EnsureClosingAsync(
        WorkflowTestScope scope,
        string accessScope,
        CancellationToken cancellationToken)
    {
        var row = await LoadScopeAsync(accessScope, scope.ScopeId, tracking: false, cancellationToken)
                   ?? throw new InvalidOperationException("The workflow test scope was not found for cleanup.");
        EnsureClosing(WorkflowTestScopeEfSupport.Read(row, accessScope, scope.ScopeId), scope);
    }

    private static void EnsureClosing(WorkflowTestScopeRecord record, WorkflowTestScope expected)
    {
        if (record.State != WorkflowTestScopeState.Closing ||
            !WorkflowTestScope.ContextEquals(record.Scope, expected))
        {
            throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");
        }
    }

    private ValueTask<WorkflowTestScopeEntity?> LoadScopeAsync(
        string accessScope,
        string scopeId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _context.WorkflowTestScopes.Where(row =>
            row.Id == WorkflowTestScopeEfSupport.Id(accessScope, scopeId));
        return tracking
            ? new(query.SingleOrDefaultAsync(cancellationToken))
            : new(query.AsNoTracking().SingleOrDefaultAsync(cancellationToken));
    }

    private ValueTask<WorkflowDispatchEntity?> LoadDispatchAsync(
        string accessScope,
        string dispatchId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _context.WorkflowDispatches.Where(row =>
            row.Id == WorkflowDispatchEfSupport.RowId(accessScope, dispatchId));
        return tracking
            ? new(query.SingleOrDefaultAsync(cancellationToken))
            : new(query.AsNoTracking().SingleOrDefaultAsync(cancellationToken));
    }

    private ValueTask<RuntimePostCommitOutboxEntity?> LoadOutboxAsync(
        string accessScope,
        string outboxItemId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _context.RuntimePostCommitOutbox.Where(row =>
            row.Id == EfRuntimePostCommitOutboxStore.RowId(accessScope, outboxItemId));
        return tracking
            ? new(query.SingleOrDefaultAsync(cancellationToken))
            : new(query.AsNoTracking().SingleOrDefaultAsync(cancellationToken));
    }

    private DispatchContinuation? DecodeContinuation(string? token, WorkflowTestScope scope)
    {
        if (token is null)
            return null;
        try
        {
            if (token.Length > MaximumContinuationTokenLength)
                throw new FormatException();
            var decoded = _codec.Decode(CursorPurpose, token);
            if (decoded.Length <= ContinuationHeaderLength ||
                decoded[0] != ContinuationTokenVersion ||
                !CryptographicOperations.FixedTimeEquals(
                    decoded.AsSpan(1, ScopeBindingLength),
                    ScopeBinding(scope)))
                throw new FormatException();

            var createdAt = new DateTimeOffset(
                BinaryPrimitives.ReadInt64BigEndian(decoded.AsSpan(1 + ScopeBindingLength, sizeof(long))),
                TimeSpan.Zero);
            var dispatchId = StrictUtf8.GetString(decoded.AsSpan(ContinuationHeaderLength));
            ValidateDispatchId(dispatchId);
            return new DispatchContinuation(createdAt, dispatchId);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            throw new ArgumentException("The workflow test-scope cleanup continuation token is invalid.", nameof(token), exception);
        }
    }

    private string EncodeContinuation(WorkflowTestScope scope, DateTimeOffset createdAt, string dispatchId)
    {
        var dispatchIdBytes = StrictUtf8.GetBytes(dispatchId);
        var payload = new byte[ContinuationHeaderLength + dispatchIdBytes.Length];
        payload[0] = ContinuationTokenVersion;
        ScopeBinding(scope).CopyTo(payload, 1);
        BinaryPrimitives.WriteInt64BigEndian(
            payload.AsSpan(1 + ScopeBindingLength, sizeof(long)),
            createdAt.UtcTicks);
        dispatchIdBytes.CopyTo(payload, ContinuationHeaderLength);
        var token = _codec.Encode(CursorPurpose, payload);
        if (token.Length > MaximumContinuationTokenLength)
            throw new InvalidOperationException("The workflow test-scope cleanup continuation token exceeds its bounded representation.");
        return token;
    }

    private static byte[] ScopeBinding(WorkflowTestScope scope)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendScopeComponent(hash, scope.ScopeId);
        AppendScopeComponent(hash, scope.TenantId);
        AppendScopeComponent(hash, scope.Partition.Value);
        Span<byte> expiry = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(expiry, scope.ExpiresAt.UtcTicks);
        hash.AppendData(expiry);
        return hash.GetHashAndReset();
    }

    private static void AppendScopeComponent(IncrementalHash hash, string? value)
    {
        var bytes = value is null ? null : StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes?.Length ?? -1);
        hash.AppendData(length);
        if (bytes is not null)
            hash.AppendData(bytes);
    }

    private string RequireScope() => EfRuntimeOperationalStoreSupport.RequireScope(_access);

    private static void ValidateDispatchId(string dispatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (dispatchId.Length > RuntimeWorkflowDispatchEfModule.IdentityMaximumLength)
            throw new ArgumentException(
                $"A workflow dispatch ID cannot exceed {RuntimeWorkflowDispatchEfModule.IdentityMaximumLength} UTF-16 code units.",
                nameof(dispatchId));
    }

    private async ValueTask RollbackAndDetachAsync(
        IDbContextTransaction transaction,
        IReadOnlyCollection<object> entities)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // Preserve the original provider failure.
        }

        foreach (var entity in entities)
            _context.Entry(entity).State = EntityState.Detached;
    }

    private sealed record DispatchContinuation(DateTimeOffset CreatedAt, string DispatchId);
}
