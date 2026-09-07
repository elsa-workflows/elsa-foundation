using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Atomically reconciles detached test-scope dispatches and deterministic cancellation outbox work.</summary>
public sealed class GroundworkV2WorkflowTestScopeCleanupStore : IWorkflowTestScopeCleanupStore
{
    private const int MaximumPageSize = GroundworkV2WorkflowTestScopeStore.MaximumPageSize;
    private const int MaximumContinuationTokenLength = 1024;
    private const byte ContinuationTokenVersion = 1;
    private const int ScopeBindingLength = 32;
    private const int ContinuationHeaderLength = 1 + ScopeBindingLength + sizeof(long);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit scopeUnit;
    private readonly StorageUnit dispatchUnit;
    private readonly StorageUnit outboxUnit;

    public GroundworkV2WorkflowTestScopeCleanupStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        scopeUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, targetName);
        dispatchUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind, targetName);
        outboxUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind, targetName);
    }

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
        if (pageSize is <= 0 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        var after = DecodeContinuation(continuationToken, scope);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(scope.TenantId);
        RequireAtomicCommit();
        EnsureClosing(scope);

        var candidates = await QueryActionableAsync(scope, after, pageSize + 1, cancellationToken);
        var page = candidates.Take(pageSize).ToArray();
        var inspected = page.Length;
        var cancelledBeforeAdmission = 0;
        var cancellationQueued = 0;
        var terminalUnchanged = 0;

        if (page.Length > 0)
        {
            using var unitOfWork = BeginAtomicUnitOfWork();
            var scopeSession = unitOfWork.OpenSession(scopeUnit);
            var dispatchSession = unitOfWork.OpenSession(dispatchUnit);
            var outboxSession = unitOfWork.OpenSession(outboxUnit);
            var scopeKey = GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scope.ScopeId));
            var scopeEntry = scopeSession.Read(scopeKey)
                ?? throw new InvalidOperationException("The workflow test scope was not found for cleanup.");
            var scopeRecord = ReadScope(scopeEntry, scope.ScopeId);
            EnsureTenant(scopeRecord.Scope.TenantId);
            if (scopeRecord.State != WorkflowTestScopeState.Closing ||
                !WorkflowTestScope.ContextEquals(scopeRecord.Scope, scope))
            {
                throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");
            }

            // The same-value scope CAS is the durable fence shared by cleanup and child admission.
            StageConditionalUpsert(
                unitOfWork,
                scopeUnit,
                GroundworkV2WorkflowTestScopeStorageConventions.Values(scopeRecord),
                scopeEntry);

            foreach (var candidate in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dispatchKey = GroundworkRuntimeRowStore.Key(
                    GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(candidate.DispatchId));
                var dispatchEntry = dispatchSession.Read(dispatchKey);
                if (dispatchEntry is null)
                {
                    terminalUnchanged++;
                    continue;
                }
                var current = ReadDispatch(dispatchEntry, candidate.DispatchId);
                EnsureTenant(current.TenantId);
                if (!WorkflowTestScope.ContextEquals(current.TestScope, scope) ||
                    current.Mode != WorkflowDispatchMode.FireAndForget)
                {
                    throw new InvalidDataException(
                        $"Workflow dispatch '{current.DispatchId}' changed immutable test-scope cleanup context.");
                }

                if (current.Status == WorkflowDispatchStatus.Pending)
                {
                    var cancelled = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(current, requestedAt);
                    StageConditionalUpsert(
                        unitOfWork,
                        dispatchUnit,
                        GroundworkV2WorkflowDispatchStorageConventions.Values(cancelled),
                        dispatchEntry);
                    cancelledBeforeAdmission++;
                    continue;
                }

                if (current.Status == WorkflowDispatchStatus.Started)
                {
                    if (!cancellationIntents.TryGetValue(current.DispatchId, out var intent))
                        throw new InvalidOperationException(
                            "A started test-scope dispatch requires deterministic cancellation responsibility.");
                    WorkflowDispatchLifecycle.ValidateTestScopeCancellationIntent(current, intent);
                    var marked = WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(current)
                        ? current
                        : WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(current, requestedAt);
                    if (!WorkflowDispatchLifecycle.RecordsEqual(current, marked))
                    {
                        StageConditionalUpsert(
                            unitOfWork,
                            dispatchUnit,
                            GroundworkV2WorkflowDispatchStorageConventions.Values(marked),
                            dispatchEntry);
                    }

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
                    var outboxKey = GroundworkRuntimeRowStore.Key(
                        GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(outboxItem.OutboxItemId));
                    var existingOutbox = outboxSession.Read(outboxKey);
                    if (existingOutbox is null)
                    {
                        unitOfWork.Stage(RowWrite.Upsert(
                            outboxUnit,
                            GroundworkV2PostCommitOutboxStorageConventions.Values(outboxItem),
                            WriteOptions.CreateOnly));
                    }
                    else
                    {
                        var existingItem = GroundworkV2PostCommitOutboxStorageConventions.Deserialize(existingOutbox.Values.Values);
                        if (!GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(existingItem, outboxItem))
                            throw new InvalidOperationException(
                                "The workflow test-scope cancellation outbox item conflicts with committed responsibility.");
                    }

                    cancellationQueued++;
                    continue;
                }

                terminalUnchanged++;
            }

            try
            {
                var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
                if (!report.IsSuccessful)
                    throw new InvalidOperationException(
                        $"Groundwork rejected workflow test-scope cleanup with {report.Failed} failed row outcomes.");
            }
            catch
            {
                try
                {
                    unitOfWork.Rollback();
                }
                catch
                {
                    // Preserve the provider's original commit failure.
                }

                throw;
            }
        }

        var remainingLive = await CountLiveAsync(scope, cancellationToken);
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
        DispatchContinuation? continuation,
        int take,
        CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        var dispatchStore = new GroundworkV2WorkflowDispatchStore(sessions, accessContextAccessor, targetName);
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            var afterCreatedAt = continuation?.CreatedAt;
            var afterDispatchId = continuation?.DispatchId;
            var branchCount = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await dispatchStore.QueryAsync(
                        new WorkflowDispatchQuery(
                            status: status,
                            take: WorkflowDispatchQuery.MaximumTake,
                            afterCreatedAt: afterCreatedAt,
                            afterDispatchId: afterDispatchId,
                            testScopeId: scope.ScopeId),
                        cancellationToken);
                foreach (var record in page)
                {
                    if (record.Mode == WorkflowDispatchMode.FireAndForget &&
                        (record.Status != WorkflowDispatchStatus.Started ||
                         !WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record)) &&
                        WorkflowTestScope.ContextEquals(record.TestScope, scope))
                    {
                        records[record.DispatchId] = record;
                        branchCount++;
                    }
                }

                if (branchCount >= take || page.Count < WorkflowDispatchQuery.MaximumTake)
                    break;
                var last = page.Last();
                afterCreatedAt = last.CreatedAt;
                afterDispatchId = last.DispatchId;
            }
        }

        return records.Values
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
    }

    private async ValueTask<int> CountLiveAsync(WorkflowTestScope scope, CancellationToken cancellationToken)
    {
        var count = 0;
        var dispatchStore = new GroundworkV2WorkflowDispatchStore(sessions, accessContextAccessor, targetName);
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            DateTimeOffset? afterCreatedAt = null;
            string? afterDispatchId = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await dispatchStore.QueryAsync(
                        new WorkflowDispatchQuery(
                            status: status,
                            take: WorkflowDispatchQuery.MaximumTake,
                            afterCreatedAt: afterCreatedAt,
                            afterDispatchId: afterDispatchId,
                            testScopeId: scope.ScopeId),
                        cancellationToken);
                count += page.Count(record =>
                    record.Mode == WorkflowDispatchMode.FireAndForget &&
                    WorkflowTestScope.ContextEquals(record.TestScope, scope));
                if (page.Count < WorkflowDispatchQuery.MaximumTake)
                    break;
                var last = page.Last();
                afterCreatedAt = last.CreatedAt;
                afterDispatchId = last.DispatchId;
            }
        }

        return count;
    }

    private static DispatchContinuation? DecodeContinuation(string? token, WorkflowTestScope scope)
    {
        if (token is null)
            return null;
        try
        {
            if (token.Length > MaximumContinuationTokenLength)
                throw new FormatException();
            var decoded = Convert.FromBase64String(token);
            if (decoded.Length <= ContinuationHeaderLength ||
                decoded[0] != ContinuationTokenVersion ||
                !StringComparer.Ordinal.Equals(Convert.ToBase64String(decoded), token) ||
                !CryptographicOperations.FixedTimeEquals(
                    decoded.AsSpan(1, ScopeBindingLength),
                    ScopeBinding(scope)))
                throw new FormatException();

            var createdAt = new DateTimeOffset(
                BinaryPrimitives.ReadInt64BigEndian(decoded.AsSpan(1 + ScopeBindingLength, sizeof(long))),
                TimeSpan.Zero);
            var dispatchId = StrictUtf8.GetString(decoded.AsSpan(ContinuationHeaderLength));
            _ = GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId);
            return new DispatchContinuation(createdAt, dispatchId);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow test-scope cleanup continuation token is invalid.", nameof(token), exception);
        }
    }

    private static string EncodeContinuation(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        string dispatchId)
    {
        var dispatchIdBytes = StrictUtf8.GetBytes(dispatchId);
        var payload = new byte[ContinuationHeaderLength + dispatchIdBytes.Length];
        payload[0] = ContinuationTokenVersion;
        ScopeBinding(scope).CopyTo(payload, 1);
        BinaryPrimitives.WriteInt64BigEndian(
            payload.AsSpan(1 + ScopeBindingLength, sizeof(long)),
            createdAt.UtcTicks);
        dispatchIdBytes.CopyTo(payload, ContinuationHeaderLength);
        var token = Convert.ToBase64String(payload);
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

    private sealed record DispatchContinuation(DateTimeOffset CreatedAt, string DispatchId);

    private IUnitOfWork BeginAtomicUnitOfWork() => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        [scopeUnit.Id.Value, dispatchUnit.Id.Value, outboxUnit.Id.Value],
        targetName);

    private void EnsureClosing(WorkflowTestScope scope)
    {
        var entry = sessions.Open(scopeUnit.Id.Value, Access, targetName).Read(
            GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scope.ScopeId)));
        if (entry is null)
            throw new InvalidOperationException("The workflow test scope was not found for cleanup.");
        var record = ReadScope(entry, scope.ScopeId);
        EnsureTenant(record.Scope.TenantId);
        if (record.State != WorkflowTestScopeState.Closing ||
            !WorkflowTestScope.ContextEquals(record.Scope, scope))
        {
            throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");
        }
    }

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability =>
                capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork workflow test-scope cleanup requires the provider's evidenced atomic-commit capability.");
        }
    }

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException("Groundwork workflow test-scope persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
                throw new InvalidOperationException(
                    "Groundwork workflow test-scope cleanup requires one explicit persistence scope; global and across-scope access are refused.");
            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void EnsureTenant(string? tenantId) => accessContextAccessor.Current.EnsureTenantScope(tenantId);

    private static WorkflowTestScopeRecord ReadScope(StoredEntry entry, string scopeId)
    {
        var record = GroundworkV2WorkflowTestScopeStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.Scope.ScopeId, scopeId))
            throw new InvalidDataException($"Groundwork workflow test-scope physical identity collision detected for '{scopeId}'.");
        return record;
    }

    private static WorkflowDispatchRecord ReadDispatch(StoredEntry entry, string dispatchId)
    {
        var record = GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, dispatchId))
            throw new InvalidDataException($"Groundwork workflow-dispatch physical identity collision detected for '{dispatchId}'.");
        return record;
    }

    private static void StageConditionalUpsert(
        IUnitOfWork unitOfWork,
        StorageUnit unit,
        StorageValues values,
        StoredEntry existing)
    {
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork row in unit '{unit.Id.Value}' did not expose an optimistic revision.");
        unitOfWork.Stage(RowWrite.ConditionalUpsert(unit, values, WriteOptions.IfVersion(revision)));
    }
}
