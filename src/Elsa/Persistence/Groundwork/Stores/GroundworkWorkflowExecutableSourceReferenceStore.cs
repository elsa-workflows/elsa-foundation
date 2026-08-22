using System.Globalization;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutableSourceReferenceStore"/> (ADR 0038/0039/0040). Each per-publish
/// source reference is one document, mirroring the WorkflowExecutable bridge. A constant collection partition
/// serves the unfiltered list. Scope, expiry, retirement, and artifact facts are lifted to flat envelope fields
/// so hot filters use declared bounded routes instead of provider-wide scans.
/// </summary>
public sealed class GroundworkWorkflowExecutableSourceReferenceStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, boundedStore), IWorkflowExecutableSourceReferenceStore
{
    public async ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceReferenceId);

        await SaveDocumentAsync(reference.SourceReferenceId, ToEnvelope(reference), cancellationToken);
    }

    public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        return LoadDocumentAsync<SourceReferenceEnvelope, WorkflowExecutableSourceReference>(
            sourceReferenceId, envelope => envelope.Reference, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
        WorkflowExecutableSourceReferenceArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryPageAsync(
            ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByArtifactQuery,
            query,
            [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, query.ArtifactId)],
            cancellationToken);
    }

    /// <remarks>
    /// The predicate is the nested <c>reference.definitionVersionId</c> dot-path rather than a lifted envelope
    /// field: the value is already serialized on every reference, so the declared route is an added index over an
    /// existing field and needs neither a document-shape change nor a re-save of the rows written before it.
    /// </remarks>
    public async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(
        WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryPageAsync(
            ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByDefinitionVersionQuery,
            query,
            [
                Equal(
                    ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDefinitionVersionIdField,
                    query.DefinitionVersionId)
            ],
            cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
        WorkflowExecutableSourceReferencePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LiveOnly)
        {
            var clauses = new List<DocumentQueryClause>
            {
                Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.FalseString),
                GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, query.Now!.Value)
            };
            var queryIdentity = ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesQuery;
            if (query.Scope is { } scope)
            {
                queryIdentity = ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesByScopeQuery;
                clauses.Insert(0, Equal(ElsaRuntimeStorageManifest.ScopeField, scope.ToString()));
            }

            if (query.Scope is null)
            {
                clauses.Insert(0, Equal(
                    ElsaRuntimeStorageManifest.CollectionField,
                    ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection));
            }

            return await QueryPageAsync(queryIdentity, query, clauses, cancellationToken);
        }

        if (query.Scope is { } requestedScope)
        {
            return await QueryPageAsync(
                ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByScopeQuery,
                query,
                [Equal(ElsaRuntimeStorageManifest.ScopeField, requestedScope.ToString())],
                cancellationToken);
        }

        return await QueryPageAsync(
            ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesQuery,
            query,
            [Equal(
                ElsaRuntimeStorageManifest.CollectionField,
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection)],
            cancellationToken);
    }

    public async ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        var reference = await FindAsync(sourceReferenceId, cancellationToken);
        if (reference is null)
            return false;

        await SaveDocumentAsync(sourceReferenceId, ToEnvelope(reference.Retire(deletedAt, reason)), cancellationToken);
        return true;
    }

    public async ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        var result = await DeleteDocumentAsync(sourceReferenceId, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var expired = await QueryBatchAsync(
            ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery,
            [LessThanOrEqual(ElsaRuntimeStorageManifest.ExpiresAtField, now)],
            batch.Limit,
            cancellationToken);
        var remaining = batch.Limit - expired.Count;
        var retired = remaining == 0
            ? []
            : await QueryBatchAsync(
                ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.TrueString),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                remaining,
                cancellationToken);
        var doomed = expired
            .Concat(retired)
            .Select(reference => reference.SourceReferenceId)
            .ToArray();

        foreach (var id in doomed)
            await DeleteDocumentAsync(id, cancellationToken);

        return doomed;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        WorkflowExecutableArtifactCandidateBatch candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var unreferenced = new List<string>(candidates.ArtifactIds.Count);
        foreach (var artifactId in candidates.ArtifactIds)
        {
            // A single finite route admission is enough: the route predicate includes both live facts and the
            // artifact identity, so provider-side existence does not materialize this artifact's references.
            var live = await QueryPageAsync(
                ElsaRuntimeStorageManifest.FindLiveWorkflowExecutableSourceReferenceByArtifactQuery,
                new RuntimeStorePageRequest(limit: 1),
                [
                    Equal(ElsaRuntimeStorageManifest.ArtifactIdField, artifactId),
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.FalseString),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                cancellationToken);
            if (live.Items.Count == 0)
                unreferenced.Add(artifactId);
        }

        return unreferenced.ToArray();
    }

    private async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> QueryPageAsync(
        string queryIdentity,
        RuntimeStorePageRequest query,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses,
                GetOrder(queryIdentity),
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);
        return new RuntimeStorePage<WorkflowExecutableSourceReference>(
            query,
            result.Documents
                .Select(Serializer.Deserialize<SourceReferenceEnvelope>)
                .Select(envelope => envelope.Reference)
                .ToArray(),
            result.NextContinuation);
    }

    private async ValueTask<IReadOnlyList<WorkflowExecutableSourceReference>> QueryBatchAsync(
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses,
                GetOrder(queryIdentity),
                take: limit),
            cancellationToken);
        return result.Documents
            .Select(Serializer.Deserialize<SourceReferenceEnvelope>)
            .Select(envelope => envelope.Reference)
            .ToArray();
    }

    private static DocumentQueryClause Equal(string fieldPath, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value));

    private static IReadOnlyList<DocumentQueryOrder> GetOrder(string queryIdentity) =>
        queryIdentity is
            ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesQuery or
            ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesByScopeQuery or
            ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery or
            ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery or
            ElsaRuntimeStorageManifest.FindLiveWorkflowExecutableSourceReferenceByArtifactQuery
            ?
            [
                new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExpiresAtField),
                new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)
            ]
            : [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)];

    private static DocumentQueryClause GreaterThan(string fieldPath, DateTimeOffset value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(
            fieldPath,
            value.ToString("O", CultureInfo.InvariantCulture)));

    private static DocumentQueryClause LessThanOrEqual(string fieldPath, DateTimeOffset value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
            fieldPath,
            value.ToString("O", CultureInfo.InvariantCulture)));

    private static SourceReferenceEnvelope ToEnvelope(WorkflowExecutableSourceReference reference) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
        reference.ArtifactId,
        reference.Scope.ToString(),
        reference.ExpiresAt ?? DateTimeOffset.MaxValue,
        (reference.DeletedAt is not null).ToString(),
        reference);

    // The constant collection partition preserves unfiltered enumeration; hot lookup and GC facts are lifted to
    // flat fields so bounded routes never depend on nested payload projection.
    private sealed record SourceReferenceEnvelope(
        string Collection,
        string ArtifactId,
        string Scope,
        DateTimeOffset ExpiresAt,
        string IsRetired,
        WorkflowExecutableSourceReference Reference);
}
