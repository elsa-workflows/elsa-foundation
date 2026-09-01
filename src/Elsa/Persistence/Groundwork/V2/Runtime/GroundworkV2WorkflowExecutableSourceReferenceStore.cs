using System.Collections.Immutable;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 source-reference store.</summary>
/// <remarks>
/// A source reference is a complete JSON sidecar around a small set of lookup projections. The adapter admits only
/// one explicit persistence scope, uses identity reads for direct access, and keeps source-reference creation strict.
/// Retirement and deletion use provider optimistic concurrency. There is no document bridge, migration, or fallback.
/// </remarks>
public sealed class GroundworkV2WorkflowExecutableSourceReferenceStore : IWorkflowExecutableSourceReferenceStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowExecutableSourceReferenceStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind, targetName);
    }

    public ValueTask SaveAsync(
        WorkflowExecutableSourceReference reference,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Validate(reference);
        EnsureTenant(reference);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(reference.SourceReferenceId);
        var existing = session.Read(key);
        if (existing is not null)
        {
            _ = Deserialize(existing.Values.Values);
            throw new InvalidOperationException(
                $"Groundwork source reference '{reference.SourceReferenceId}' already exists; source references are create-only.");
        }

        var values = GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Values(reference);
        var result = session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                $"Groundwork source-reference save for '{reference.SourceReferenceId}' did not succeed; retry the operation.");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stages this source reference's creation into a transaction the caller owns, for an operation that
    /// is one act across lanes. Create-only, as the lane's own save is: the caller resolves an existing
    /// reference before staging.
    /// </summary>
    public static void StageCreate(GroundworkStorageTransaction transaction, WorkflowExecutableSourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.StageInsert(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Values(reference),
            WriteOptions.CreateOnly);
    }

    public ValueTask<WorkflowExecutableSourceReference?> FindAsync(
        string sourceReferenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(sourceReferenceId));
        if (entry is null)
            return ValueTask.FromResult<WorkflowExecutableSourceReference?>(null);

        var reference = Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(reference.SourceReferenceId, sourceReferenceId))
            throw new InvalidDataException("Groundwork source-reference row identity does not match its requested key.");
        return ValueTask.FromResult<WorkflowExecutableSourceReference?>(reference);
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
        WorkflowExecutableSourceReferenceArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var artifact = Column(table, ElsaRuntimeV2StorageManifest.ArtifactIdField);
        var sourceReference = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(artifact, query.ArtifactId),
            [new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));
        return ValueTask.FromResult(Page(query, result));
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(
        WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var definitionVersion = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionVersionIdField);
        var sourceReference = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(definitionVersion, query.DefinitionVersionId),
            [new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));
        return ValueTask.FromResult(Page(query, result));
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
        WorkflowExecutableSourceReferencePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var predicates = new List<Predicate>();
        if (query.Scope is { } scope)
            predicates.Add(Equal(Column(table, ElsaRuntimeV2StorageManifest.ScopeField), scope.ToString()));
        else
        {
            predicates.Add(Equal(
                Column(table, ElsaRuntimeV2StorageManifest.CollectionField),
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind));
        }

        if (query.LiveOnly)
        {
            predicates.Add(Equal(Column(table, ElsaRuntimeV2StorageManifest.IsRetiredField), false));
            predicates.Add(new Predicate.Range(
                Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField),
                Bound.Exclusive(QueryConstant.Of(
                    Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField), query.Now!.Value)),
                null));
        }

        var expiresAt = Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField);
        var sourceReference = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var order = query.LiveOnly
            ? ImmutableArray.Create(
                new OrderTerm(expiresAt, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last))
            : ImmutableArray.Create(new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last));
        var result = Open().Query(new QueryRequest(
            table,
            And(predicates),
            order,
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));
        return ValueTask.FromResult(Page(query, result));
    }

    public async ValueTask<bool> RetireAsync(
        string sourceReferenceId,
        DateTimeOffset deletedAt,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(sourceReferenceId);
        var existing = session.Read(key);
        if (existing is null)
            return false;

        var reference = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(reference.SourceReferenceId, sourceReferenceId))
            throw new InvalidDataException("Groundwork source-reference row identity does not match its requested key.");
        if (reference.DeletedAt is not null)
            return true;

        var updated = reference.Retire(deletedAt, reason);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork source-reference row '{sourceReferenceId}' did not expose an optimistic revision.");
        var result = ConditionalUpsert(
            session,
            GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Values(updated),
            revision);
        if (result.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
            return false;
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                $"Groundwork source-reference retirement for '{sourceReferenceId}' failed with status '{result.Status}'.");
        }

        return true;
    }

    public ValueTask<bool> TryRestoreAsync(
        WorkflowExecutableSourceReference expectedRetiredReference,
        WorkflowExecutableSourceReference restoredReference,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Validate(expectedRetiredReference);
        GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Validate(restoredReference);
        EnsureTenant(expectedRetiredReference);
        EnsureTenant(restoredReference);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRetiredReference.DeletedAt is null ||
            restoredReference.DeletedAt is not null ||
            !WorkflowExecutableSourceReferenceComparer.SameIdentity(expectedRetiredReference, restoredReference))
            return ValueTask.FromResult(false);

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(expectedRetiredReference.SourceReferenceId);
        var existing = session.Read(key);
        if (existing is null)
            return ValueTask.FromResult(false);

        var current = Deserialize(existing.Values.Values);
        if (!WorkflowExecutableSourceReferenceComparer.SameSnapshot(current, expectedRetiredReference))
            return ValueTask.FromResult(false);

        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork source-reference row '{expectedRetiredReference.SourceReferenceId}' did not expose an optimistic revision.");
        var result = ConditionalUpsert(
            session,
            GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Values(restoredReference),
            revision);
        if (result.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
            return ValueTask.FromResult(false);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                $"Groundwork source-reference restoration for '{expectedRetiredReference.SourceReferenceId}' failed with status '{result.Status}'.");
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> DeleteAsync(
        string sourceReferenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(sourceReferenceId);
        var existing = session.Read(key);
        if (existing is null)
            return ValueTask.FromResult(false);

        var reference = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(reference.SourceReferenceId, sourceReferenceId))
            throw new InvalidDataException("Groundwork source-reference row identity does not match its requested key.");
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork source-reference row '{sourceReferenceId}' did not expose an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        if (result.Status is not (WriteOutcomeStatus.Deleted or WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound))
        {
            throw new InvalidOperationException(
                $"Groundwork source-reference delete for '{sourceReferenceId}' failed with status '{result.Status}'.");
        }

        return ValueTask.FromResult(result.Status == WriteOutcomeStatus.Deleted);
    }

    public async ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        var expired = QueryCleanup(
            new Predicate.Range(
                Column(new TableId(unit.Name), ElsaRuntimeV2StorageManifest.ExpiresAtField),
                null,
                Bound.Inclusive(QueryConstant.Of(
                    Column(new TableId(unit.Name), ElsaRuntimeV2StorageManifest.ExpiresAtField), now))),
            batch.Limit,
            cancellationToken);
        var remaining = batch.Limit - expired.Count;
        var retired = remaining == 0
            ? []
            : QueryCleanup(
                And([
                    Equal(Column(new TableId(unit.Name), ElsaRuntimeV2StorageManifest.IsRetiredField), true),
                    new Predicate.Range(
                        Column(new TableId(unit.Name), ElsaRuntimeV2StorageManifest.ExpiresAtField),
                        Bound.Exclusive(QueryConstant.Of(
                            Column(new TableId(unit.Name), ElsaRuntimeV2StorageManifest.ExpiresAtField), now)),
                        null)
                ]),
                remaining,
                cancellationToken);

        var deleted = new List<string>(expired.Count + retired.Count);
        foreach (var reference in expired.Concat(retired))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await FindAsync(reference.SourceReferenceId, cancellationToken);
            if (current is null || (!current.IsExpired(now) && current.DeletedAt is null))
                continue;
            if (await DeleteAsync(reference.SourceReferenceId, cancellationToken))
                deleted.Add(reference.SourceReferenceId);
        }

        return deleted;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        WorkflowExecutableArtifactCandidateBatch candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var artifact = Column(table, ElsaRuntimeV2StorageManifest.ArtifactIdField);
        var retired = Column(table, ElsaRuntimeV2StorageManifest.IsRetiredField);
        var expiresAt = Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField);
        var sourceReference = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var unreferenced = new List<string>(candidates.ArtifactIds.Count);
        foreach (var artifactId in candidates.ArtifactIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Open().Query(new QueryRequest(
                table,
                And([
                    Equal(artifact, artifactId),
                    Equal(retired, false),
                    new Predicate.Range(
                        expiresAt,
                        Bound.Exclusive(QueryConstant.Of(expiresAt, now)),
                        null)
                ]),
                [new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                Paging.Keyset(1)));
            if (result.Rows.Count == 0)
                unreferenced.Add(artifactId);
        }

        return unreferenced;
    }

    private List<WorkflowExecutableSourceReference> QueryCleanup(
        Predicate predicate,
        int limit,
        CancellationToken cancellationToken)
    {
        var table = new TableId(unit.Name);
        var expiresAt = Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField);
        var sourceReference = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var result = Open().Query(new QueryRequest(
            table,
            predicate,
            [
                new OrderTerm(expiresAt, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(sourceReference, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(limit)));
        cancellationToken.ThrowIfCancellationRequested();
        return result.Rows.Select(Deserialize).ToList();
    }

    private RuntimeStorePage<WorkflowExecutableSourceReference> Page(
        RuntimeStorePageRequest query,
        QueryMaterializedResult result)
    {
        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
            throw new InvalidDataException("Groundwork source-reference query returned a continuation after an empty page.");
        return new RuntimeStorePage<WorkflowExecutableSourceReference>(
            query,
            result.Rows.Select(Deserialize).ToArray(),
            result.NextContinuationToken);
    }

    private IStorageSession Open()
    {
        var context = AccessContext;
        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope!.Value)),
            targetName);
    }

    private PersistenceAccessContext AccessContext
    {
        get
        {
            var context = accessContextAccessor.Current;
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork source-reference access requires one explicit persistence scope; global and across-scope access are refused.");
            }

            return context;
        }
    }

    private WorkflowExecutableSourceReference Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var reference = GroundworkV2WorkflowExecutableSourceReferenceStorageConventions.Deserialize(values);
        EnsureTenant(reference);
        return reference;
    }

    private void EnsureTenant(WorkflowExecutableSourceReference reference) =>
        AccessContext.EnsureTenantScope(reference.TenantId);

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic source-reference concurrency.");
        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork source-reference unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork source-reference query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Equal(ColumnRef column, bool value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate And(IReadOnlyList<Predicate> predicates) =>
        predicates.Count == 1 ? predicates[0] : new Predicate.And(predicates);

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;
}
