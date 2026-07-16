using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutableSourceReferenceStore"/> (ADR 0038/0039/0040). Each per-publish
/// source reference is one document, mirroring the WorkflowExecutable bridge. A constant collection partition
/// serves the unfiltered list and the expiry/retirement GC sweep through an equality index (Groundwork is
/// equality-only, so expiry filtering happens in memory); the by-artifact index serves ListByArtifact and the
/// GC unreferenced-artifact check.
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
        var references = await ListAllAsync(cancellationToken);

        return references
            .Where(reference => scope is null || reference.Scope == scope)
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
        var doomed = (await ListAllAsync(cancellationToken))
            .Where(reference => reference.DeletedAt is not null || reference.IsExpired(now))
            .Select(reference => reference.SourceReferenceId)
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

        var liveArtifactIds = (await ListAllAsync(cancellationToken))
            .Where(reference => reference.IsLive(now))
            .Select(reference => reference.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.Where(artifactId => !liveArtifactIds.Contains(artifactId)).ToArray();
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

    private static SourceReferenceEnvelope ToEnvelope(WorkflowExecutableSourceReference reference) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
        reference.ArtifactId,
        reference);

    // The constant collection partition lets the list/expiry sweep use a keyword equality index instead of a
    // provider-wide scan; ArtifactId is lifted to the top level so the by-artifact query hits a flat index path.
    private sealed record SourceReferenceEnvelope(string Collection, string ArtifactId, WorkflowExecutableSourceReference Reference);
}
