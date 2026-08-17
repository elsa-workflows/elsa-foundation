using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 workflow-dispatch lifecycle store.</summary>
/// <remarks>
/// The adapter uses one public Groundwork session for ordinary operations and one public unit of work
/// for test-scoped admission. It preserves the provider-neutral dispatch lifecycle validator, scopes
/// every operation explicitly, and keeps direct/query/delete paths on the same row identity used by the
/// v2 checkpoint writer.
/// </remarks>
public sealed class GroundworkV2WorkflowDispatchStore :
    IWorkflowDispatchStore,
    IWorkflowDispatchQueryStore,
    IWorkflowDispatchDeleteStore,
    IWorkflowDispatchRetentionRootStore,
    IWorkflowDispatchAdmissionStore,
    IWorkflowDispatchCancellationStore
{
    private const int MaxTransitionAttempts = 16;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowDispatchStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind, targetName);
    }

    public ValueTask<WorkflowDispatchRecord> SaveAsync(
        WorkflowDispatchRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(record.TenantId);
        var physicalId = GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(record.DispatchId);
        var values = GroundworkV2WorkflowDispatchStorageConventions.Values(record);
        var session = Open();

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(GroundworkRuntimeRowStore.Key(physicalId));
            if (existing is null)
            {
                WorkflowDispatchLifecycle.ValidateNew(record);
                var inserted = session.Insert(values, WriteOptions.CreateOnly);
                if (IsSaved(inserted.Status))
                    return ValueTask.FromResult(record);
                if (inserted.Status != WriteOutcomeStatus.ConcurrencyConflict)
                {
                    throw new InvalidOperationException(
                        $"Groundwork rejected workflow dispatch '{record.DispatchId}' with status '{inserted.Status}'.");
                }

                continue;
            }

            var current = Read(existing, record.DispatchId);
            EnsureTenant(current.TenantId);
            WorkflowDispatchLifecycle.ValidateTransition(current, record);
            if (WorkflowDispatchLifecycle.RecordsEqual(current, record))
                return ValueTask.FromResult(current);

            var revision = existing.Version ?? throw new InvalidDataException(
                $"Groundwork workflow-dispatch row '{record.DispatchId}' did not expose an optimistic revision.");
            var updated = ConditionalUpsert(session, values, revision);
            if (IsSaved(updated.Status))
                return ValueTask.FromResult(record);
            if (updated.Status != WriteOutcomeStatus.ConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow dispatch '{record.DispatchId}' with status '{updated.Status}'.");
            }
        }

        throw new InvalidOperationException(
            $"Groundwork workflow dispatch '{record.DispatchId}' changed concurrently and did not settle.");
    }

    public ValueTask<WorkflowDispatchRecord?> FindAsync(
        string dispatchId,
        CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId)));
        if (entry is null)
            return ValueTask.FromResult<WorkflowDispatchRecord?>(null);

        var record = Read(entry, dispatchId);
        EnsureTenant(record.TenantId);
        return ValueTask.FromResult<WorkflowDispatchRecord?>(record);
    }

    public async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        if (admittedAt == default)
            throw new ArgumentOutOfRangeException(nameof(admittedAt), "Child admission requires a recorded timestamp.");
        cancellationToken.ThrowIfCancellationRequested();

        var initial = Load(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
        EnsureTenant(initial.Record.TenantId);
        if (initial.Record.TestScope is not null && initial.Record.Mode == WorkflowDispatchMode.FireAndForget)
            return await TryAdmitTestScopedAsync(dispatchId, admittedAt, cancellationToken);

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId));
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(key)
                ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
            var record = Read(existing, dispatchId);
            EnsureTenant(record.TenantId);
            if (record.Status == WorkflowDispatchStatus.Started)
                return new WorkflowDispatchAdmissionResult(
                    WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, record);
            if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(record))
                return new WorkflowDispatchAdmissionResult(
                    WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, record);
            if (record.Status != WorkflowDispatchStatus.Pending)
                return new WorkflowDispatchAdmissionResult(
                    WorkflowDispatchAdmissionDisposition.Terminal, record);

            var effectiveAt = admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt;
            var candidate = record.TransitionTo(WorkflowDispatchStatus.Started, effectiveAt);
            var revision = existing.Version ?? throw new InvalidDataException(
                $"Groundwork workflow-dispatch row '{dispatchId}' did not expose an optimistic revision.");
            var result = ConditionalUpsert(session, GroundworkV2WorkflowDispatchStorageConventions.Values(candidate), revision);
            if (IsSaved(result.Status))
                return new WorkflowDispatchAdmissionResult(
                    WorkflowDispatchAdmissionDisposition.Admitted, candidate);
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow dispatch admission '{dispatchId}' with status '{result.Status}'.");
            }
        }

        throw new InvalidOperationException($"Groundwork workflow dispatch '{dispatchId}' changed concurrently and did not settle.");
    }

    public ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(
        WorkflowDispatchCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(request.DispatchId));

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(key)
                ?? throw new InvalidOperationException(
                    $"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            var record = Read(existing, request.DispatchId);
            EnsureTenant(record.TenantId);
            ValidateCancellationIdentity(record, request);

            if (record.Status is not (WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started))
            {
                return ValueTask.FromResult(new WorkflowDispatchCancellationResult(
                    WorkflowDispatchCancellationDisposition.TerminalUnchanged, record));
            }

            var candidate = record.Status == WorkflowDispatchStatus.Pending
                ? WorkflowDispatchLifecycle.CancelBeforeAdmission(record, request.RequestedAt)
                : WorkflowDispatchLifecycle.MarkCancellationRequested(record, request.RequestedAt);
            var disposition = record.Status == WorkflowDispatchStatus.Pending
                ? WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission
                : WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission;
            if (WorkflowDispatchLifecycle.RecordsEqual(record, candidate))
                return ValueTask.FromResult(new WorkflowDispatchCancellationResult(disposition, record));

            var revision = existing.Version ?? throw new InvalidDataException(
                $"Groundwork workflow-dispatch row '{request.DispatchId}' did not expose an optimistic revision.");
            var result = ConditionalUpsert(session, GroundworkV2WorkflowDispatchStorageConventions.Values(candidate), revision);
            if (IsSaved(result.Status))
                return ValueTask.FromResult(new WorkflowDispatchCancellationResult(disposition, candidate));
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow dispatch cancellation '{request.DispatchId}' with status '{result.Status}'.");
            }
        }

        throw new InvalidOperationException(
            $"Groundwork workflow dispatch '{request.DispatchId}' changed concurrently and did not settle.");
    }

    public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
        string parentWorkflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var records = new List<WorkflowDispatchRecord>();
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = QueryProviderPage(
                new WorkflowDispatchQuery(
                    parentWorkflowExecutionId: parentWorkflowExecutionId,
                    take: WorkflowDispatchQuery.MaximumTake,
                    afterCreatedAt: afterCreatedAt,
                    afterDispatchId: afterDispatchId),
                cancellationToken,
                LexicographicContinuation: afterCreatedAt is not null);
            records.AddRange(page);
            if (page.Count < WorkflowDispatchQuery.MaximumTake)
                break;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }

        return ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>(records);
    }

    public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.ChildWorkflowExecutionId is not null)
        {
            var unique = QueryProviderPage(query, cancellationToken);
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>(unique.Take(query.Take).ToArray());
        }

        if (query.AfterCreatedAt is null)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>(
                QueryProviderPage(query, cancellationToken));

        var sameTimestamp = QueryProviderPage(
            query,
            cancellationToken,
            SameTimestampContinuation: true);
        var laterTimestamp = QueryProviderPage(
            query,
            cancellationToken,
            SameTimestampContinuation: false);
        var records = sameTimestamp
            .Concat(laterTimestamp)
            .Where(record => Matches(record, query))
            .GroupBy(record => record.DispatchId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                if (group.Skip(1).Any(candidate => !WorkflowDispatchLifecycle.RecordsEqual(first, candidate)))
                {
                    throw new InvalidDataException(
                        $"Groundwork returned conflicting workflow-dispatch rows for '{first.DispatchId}'.");
                }

                return first;
            })
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(query.Take)
            .ToArray();
        foreach (var record in records)
            EnsureTenant(record.TenantId);
        return ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>(records);
    }

    public ValueTask<bool> TryDeleteAsync(
        WorkflowDispatchRecord expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(expected.TenantId);
        if (!WorkflowDispatchLifecycle.IsTerminal(expected.Status))
            return ValueTask.FromResult(false);

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(expected.DispatchId));
        var existing = session.Read(key);
        if (existing is null)
            return ValueTask.FromResult(true);
        var current = Read(existing, expected.DispatchId);
        EnsureTenant(current.TenantId);
        if (!WorkflowDispatchLifecycle.RecordsEqual(current, expected) ||
            !WorkflowDispatchLifecycle.IsTerminal(current.Status))
        {
            return ValueTask.FromResult(false);
        }

        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork workflow-dispatch row '{expected.DispatchId}' did not expose an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        return result.Status switch
        {
            WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound => ValueTask.FromResult(true),
            WriteOutcomeStatus.ConcurrencyConflict => ValueTask.FromResult(false),
            _ => throw new InvalidOperationException(
                $"Groundwork rejected deletion of workflow dispatch '{expected.DispatchId}' with status '{result.Status}'.")
        };
    }

    public ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId));
        var existing = session.Read(key);
        if (existing is null)
            return ValueTask.CompletedTask;
        var current = Read(existing, dispatchId);
        EnsureTenant(current.TenantId);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork workflow-dispatch row '{dispatchId}' did not expose an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        if (result.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound)
            return ValueTask.CompletedTask;
        throw new InvalidOperationException(
            $"Groundwork rejected deletion of workflow dispatch '{dispatchId}' with status '{result.Status}'.");
    }

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = QueryAllByStatus(WorkflowDispatchStatus.Pending, cancellationToken)
            .Concat(QueryAllByStatus(WorkflowDispatchStatus.Started, cancellationToken));
        return ValueTask.FromResult<IReadOnlyCollection<string>>(
            records.Select(record => record.ChildExecutable.ArtifactId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitTestScopedAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = sessions.BeginUnitOfWork(
            Access,
            BatchWriteOptions.Exact,
            [
                ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind
            ],
            targetName);
        var scopeSession = unitOfWork.OpenSession(sessions.Unit(
            ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, targetName));
        var dispatchSession = unitOfWork.OpenSession(unit);
        var dispatchKey = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId));
        var dispatchEntry = dispatchSession.Read(dispatchKey)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
        var record = Read(dispatchEntry, dispatchId);
        EnsureTenant(record.TenantId);
        if (record.Status == WorkflowDispatchStatus.Started)
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, record);
        if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(record))
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.Terminal, record);

        var effectiveAt = admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt;
        var scopeEntry = scopeSession.Read(GroundworkRuntimeRowStore.Key(record.TestScope!.ScopeId));
        WorkflowDispatchRecord candidate;
        WorkflowDispatchAdmissionDisposition disposition;
        if (scopeEntry is not null &&
            IsOpenTestScope(scopeEntry, record.TestScope, admittedAt))
        {
            candidate = record.TransitionTo(WorkflowDispatchStatus.Started, effectiveAt);
            disposition = WorkflowDispatchAdmissionDisposition.Admitted;
            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, targetName),
                scopeEntry.Values,
                WriteOptions.IfVersion(scopeEntry.Version ?? throw new InvalidDataException(
                    $"Workflow test scope '{record.TestScope.ScopeId}' did not expose an optimistic revision."))));
        }
        else
        {
            candidate = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(record, effectiveAt);
            disposition = WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission;
        }

        unitOfWork.Stage(RowWrite.ConditionalUpsert(
            unit,
            GroundworkV2WorkflowDispatchStorageConventions.Values(candidate),
            WriteOptions.IfVersion(dispatchEntry.Version ?? throw new InvalidDataException(
                $"Workflow dispatch '{dispatchId}' did not expose an optimistic revision."))));
        try
        {
            var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            if (!report.IsSuccessful)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow-dispatch admission with {report.Failed} failed row outcomes.");
            }
        }
        catch
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's original failure.
            }

            throw;
        }

        return new WorkflowDispatchAdmissionResult(disposition, candidate);
    }

    private IReadOnlyCollection<WorkflowDispatchRecord> QueryProviderPage(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken,
        bool? SameTimestampContinuation = null,
        bool LexicographicContinuation = false)
    {
        var table = new TableId(unit.Name);
        var predicates = new List<Predicate>();
        AddEqual(predicates, table, ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField, query.ParentWorkflowExecutionId);
        AddEqual(predicates, table, ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField, query.ChildWorkflowExecutionId);
        AddEqual(predicates, table, ElsaRuntimeV2StorageManifest.StatusField, query.Status?.ToString());
        AddEqual(predicates, table, ElsaRuntimeV2StorageManifest.TestScopeIdField, query.TestScopeId);

        var createdAt = Column(table, ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField);
        var dispatchId = Column(table, ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField);
        if (LexicographicContinuation)
        {
            predicates.Add(new Predicate.Or([
                new Predicate.Range(createdAt, Bound.Exclusive(QueryConstant.Of(createdAt, query.AfterCreatedAt!.Value)), null),
                new Predicate.And([
                    new Predicate.Equal(createdAt, QueryConstant.Of(createdAt, query.AfterCreatedAt.Value)),
                    new Predicate.Range(dispatchId, Bound.Exclusive(QueryConstant.Of(dispatchId, query.AfterDispatchId!)), null)
                ])]));
        }
        else if (SameTimestampContinuation is true)
        {
            predicates.Add(new Predicate.Equal(createdAt, QueryConstant.Of(createdAt, query.AfterCreatedAt!.Value)));
            predicates.Add(new Predicate.Range(
                dispatchId,
                Bound.Exclusive(QueryConstant.Of(dispatchId, query.AfterDispatchId!)),
                null));
        }
        else if (SameTimestampContinuation is false)
        {
            predicates.Add(new Predicate.Range(
                createdAt,
                Bound.Exclusive(QueryConstant.Of(createdAt, query.AfterCreatedAt!.Value)),
                null));
        }

        var predicate = predicates.Count switch
        {
            0 => Predicate.AlwaysTrue.Instance,
            1 => predicates[0],
            _ => new Predicate.And(predicates)
        };
        var result = Open().Query(new QueryRequest(
            table,
            predicate,
            [
                new OrderTerm(createdAt, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(dispatchId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(query.Take)));
        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
        {
            throw new InvalidOperationException(
                "Groundwork workflow-dispatch query returned a continuation after an empty page.");
        }

        return result.Rows
            .Select(values => GroundworkV2WorkflowDispatchStorageConventions.Deserialize(values))
            .Where(record => Matches(record, query))
            .Select(record => EnsureTenantAndReturn(record))
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyCollection<WorkflowDispatchRecord> QueryAllByStatus(
        WorkflowDispatchStatus status,
        CancellationToken cancellationToken)
    {
        var records = new List<WorkflowDispatchRecord>();
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (true)
        {
            var query = new WorkflowDispatchQuery(
                status: status,
                take: WorkflowDispatchQuery.MaximumTake,
                afterCreatedAt: afterCreatedAt,
                afterDispatchId: afterDispatchId);
            var page = QueryProviderPage(
                query,
                cancellationToken,
                LexicographicContinuation: afterCreatedAt is not null);
            records.AddRange(page);
            if (page.Count < WorkflowDispatchQuery.MaximumTake)
                return records;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }
    }

    private LoadedDispatch? Load(string dispatchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId)));
        return entry is null ? null : new LoadedDispatch(Read(entry, dispatchId), entry.Version ?? throw new InvalidDataException(
            $"Groundwork workflow-dispatch row '{dispatchId}' did not expose an optimistic revision."));
    }

    private IStorageSession Open() => sessions.Open(unit.Id.Value, Access, targetName);

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException("Workflow-dispatch persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork workflow dispatches require one explicit persistence scope; global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void EnsureTenant(string? tenantId) => accessContextAccessor.Current.EnsureTenantScope(tenantId);

    private WorkflowDispatchRecord EnsureTenantAndReturn(WorkflowDispatchRecord record)
    {
        EnsureTenant(record.TenantId);
        return record;
    }

    private static WorkflowDispatchRecord Read(StoredEntry entry, string requestedId)
    {
        var record = GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, requestedId))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-dispatch physical identity collision detected for '{requestedId}'.");
        }

        return record;
    }

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow-dispatch concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private void AddEqual(
        ICollection<Predicate> predicates,
        TableId table,
        string field,
        string? value)
    {
        if (value is null)
            return;
        var column = Column(table, field);
        predicates.Add(new Predicate.Equal(column, QueryConstant.Of(column, value)));
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork workflow-dispatch unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow-dispatch query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static bool Matches(WorkflowDispatchRecord record, WorkflowDispatchQuery query) =>
        (query.ParentWorkflowExecutionId is null || StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, query.ParentWorkflowExecutionId)) &&
        (query.ChildWorkflowExecutionId is null || StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, query.ChildWorkflowExecutionId)) &&
        (query.Status is null || record.Status == query.Status) &&
        (query.TestScopeId is null || StringComparer.Ordinal.Equals(record.TestScope?.ScopeId, query.TestScopeId)) &&
        (query.AfterCreatedAt is null ||
         record.CreatedAt > query.AfterCreatedAt ||
         record.CreatedAt == query.AfterCreatedAt && StringComparer.Ordinal.Compare(record.DispatchId, query.AfterDispatchId) > 0);

    private static void ValidateCancellationIdentity(
        WorkflowDispatchRecord record,
        WorkflowDispatchCancellationRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.DispatchId, request.DispatchId) ||
            !StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId) ||
            record.Mode != WorkflowDispatchMode.WaitForCompletion ||
            !WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(record))
        {
            throw new InvalidOperationException(
                $"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with its durable dispatch record.");
        }
    }

    private static bool IsOpenTestScope(
        StoredEntry entry,
        WorkflowTestScope expected,
        DateTimeOffset observedAt)
    {
        var actual = Deserialize<WorkflowTestScopeRecord>(entry.Values.Values);
        return actual.State == WorkflowTestScopeState.Open &&
               !actual.Scope.IsExpired(observedAt) &&
               WorkflowTestScope.ContextEquals(actual.Scope, expected);
    }

    private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(ElsaRuntimeV2StorageManifest.SchemaVersionField, out var rawVersion) ||
            !TryReadString(rawVersion, out var schemaVersion) ||
            !StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException("Groundwork workflow test-scope row returned an unsupported schema version.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                System.Text.Json.JsonElement element => element.GetRawText(),
                System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow test-scope row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow test-scope row did not contain JSON content.");
        return GroundworkV2RuntimeJson.Deserialize<T>(content)
               ?? throw new InvalidDataException("Groundwork workflow test-scope row content was empty.");
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue when !string.IsNullOrWhiteSpace(stringValue):
                text = stringValue;
                return true;
            case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element
                when !string.IsNullOrWhiteSpace(element.GetString()):
                text = element.GetString()!;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool IsSaved(WriteOutcomeStatus status) => status is
        WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;

    private static void ValidateDispatchId(string dispatchId) =>
        GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId);

    private sealed record LoadedDispatch(WorkflowDispatchRecord Record, long Version);
}
