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

    public async ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var references = await QueryReferencesAsync(
            ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByArtifactQuery,
            ElsaRuntimeStorageManifest.ArtifactIdField,
            artifactId,
            cancellationToken);

        return references.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(
        WorkflowExecutableReferenceScope? scope = null,
        bool liveOnly = false,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = now ?? DateTimeOffset.UtcNow;
        var references = scope is { } requestedScope
            ? await QueryReferencesAsync(
                ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByScopeQuery,
                ElsaRuntimeStorageManifest.ScopeField,
                requestedScope.ToString(),
                cancellationToken)
            : await ListAllAsync(cancellationToken);

        return references
            .Where(reference => !liveOnly || reference.IsLive(asOf))
            .ToArray();
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

    public async ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expired = await QueryDateReferencesAsync(
            ElsaRuntimeStorageManifest.ListExpiredWorkflowExecutableSourceReferencesQuery,
            ElsaRuntimeStorageManifest.ExpiresAtField,
            now,
            cancellationToken);
        var retired = await QueryReferencesAsync(
            ElsaRuntimeStorageManifest.ListRetiredWorkflowExecutableSourceReferencesQuery,
            ElsaRuntimeStorageManifest.IsRetiredField,
            bool.TrueString,
            cancellationToken);
        var doomed = expired
            .Concat(retired)
            .Select(reference => reference.SourceReferenceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var id in doomed)
            await DeleteDocumentAsync(id, cancellationToken);

        return doomed;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        IEnumerable<string> artifactIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactIds);
        var candidates = artifactIds.Distinct(StringComparer.Ordinal).ToArray();

        var unreferenced = new List<string>(candidates.Length);
        foreach (var artifactId in candidates)
        {
            var hasLiveReference = (await ListByArtifactAsync(artifactId, cancellationToken))
                .Any(reference => reference.IsLive(now));
            if (!hasLiveReference)
                unreferenced.Add(artifactId);
        }

        return unreferenced.ToArray();
    }

    private async ValueTask<IReadOnlyList<WorkflowExecutableSourceReference>> ListAllAsync(CancellationToken cancellationToken) =>
        await QueryReferencesAsync(
            ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
            cancellationToken);

    private async ValueTask<IReadOnlyList<WorkflowExecutableSourceReference>> QueryReferencesAsync(
        string queryIdentity,
        string fieldPath,
        string value,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))]),
            cancellationToken);
        return result.Documents
            .Select(Serializer.Deserialize<SourceReferenceEnvelope>)
            .Select(envelope => envelope.Reference)
            .ToArray();
    }

    private async ValueTask<IReadOnlyList<WorkflowExecutableSourceReference>> QueryDateReferencesAsync(
        string queryIdentity,
        string fieldPath,
        DateTimeOffset value,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    fieldPath,
                    value.ToString("O", CultureInfo.InvariantCulture)))],
                [new DocumentQueryOrder(fieldPath)]),
            cancellationToken);
        return result.Documents
            .Select(Serializer.Deserialize<SourceReferenceEnvelope>)
            .Select(envelope => envelope.Reference)
            .ToArray();
    }

    private static SourceReferenceEnvelope ToEnvelope(WorkflowExecutableSourceReference reference) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
        reference.ArtifactId,
        reference.Scope.ToString(),
        reference.ExpiresAt,
        (reference.DeletedAt is not null).ToString(),
        reference);

    // The constant collection partition preserves unfiltered enumeration; hot lookup and GC facts are lifted to
    // flat fields so bounded routes never depend on nested payload projection.
    private sealed record SourceReferenceEnvelope(
        string Collection,
        string ArtifactId,
        string Scope,
        DateTimeOffset? ExpiresAt,
        string IsRetired,
        WorkflowExecutableSourceReference Reference);
}
