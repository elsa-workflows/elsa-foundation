using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutableSourceReferenceStore : IWorkflowExecutableSourceReferenceStore
{
    private readonly Dictionary<string, WorkflowExecutableSourceReference> _references = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceReferenceId);

        lock (_gate)
            _references[reference.SourceReferenceId] = reference;

        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        lock (_gate)
            return ValueTask.FromResult(_references.GetValueOrDefault(sourceReferenceId));
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
        WorkflowExecutableSourceReferenceArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
            return ValueTask.FromResult(Page(
                query,
                $"source-reference:artifact:{query.ArtifactId}",
                _references.Values.Where(reference => string.Equals(reference.ArtifactId, query.ArtifactId, StringComparison.Ordinal))));
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
        WorkflowExecutableSourceReferencePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
            return ValueTask.FromResult(Page(
                query,
                $"source-reference:scope:{query.Scope?.ToString() ?? "*"}:live:{query.LiveOnly}:asof:{query.Now?.ToUniversalTime():O}",
                _references.Values
                    .Where(reference => query.Scope is null || reference.Scope == query.Scope)
                    .Where(reference => !query.LiveOnly || reference.IsLive(query.Now!.Value))));
    }

    public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        lock (_gate)
        {
            if (!_references.TryGetValue(sourceReferenceId, out var reference))
                return ValueTask.FromResult(false);

            _references[sourceReferenceId] = reference.Retire(deletedAt, reason);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> TryRestoreAsync(
        WorkflowExecutableSourceReference expectedRetiredReference,
        WorkflowExecutableSourceReference restoredReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedRetiredReference);
        ArgumentNullException.ThrowIfNull(restoredReference);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRetiredReference.DeletedAt is null ||
            restoredReference.DeletedAt is not null ||
            !SameReferenceIdentity(expectedRetiredReference, restoredReference))
            return ValueTask.FromResult(false);

        lock (_gate)
        {
            if (!_references.TryGetValue(expectedRetiredReference.SourceReferenceId, out var current) ||
                !SameReferenceSnapshot(current, expectedRetiredReference))
                return ValueTask.FromResult(false);

            _references[expectedRetiredReference.SourceReferenceId] = restoredReference;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        lock (_gate)
            return ValueTask.FromResult(_references.Remove(sourceReferenceId));
    }

    private static bool SameReferenceSnapshot(
        WorkflowExecutableSourceReference current,
        WorkflowExecutableSourceReference expected) =>
        SameReferenceIdentity(current, expected) &&
        current.DeletedAt == expected.DeletedAt &&
        StringComparer.Ordinal.Equals(current.DeletedReason, expected.DeletedReason);

    private static bool SameReferenceIdentity(
        WorkflowExecutableSourceReference current,
        WorkflowExecutableSourceReference expected) =>
        StringComparer.Ordinal.Equals(current.SourceReferenceId, expected.SourceReferenceId) &&
        StringComparer.Ordinal.Equals(current.ArtifactId, expected.ArtifactId) &&
        StringComparer.Ordinal.Equals(current.SourceKind, expected.SourceKind) &&
        StringComparer.Ordinal.Equals(current.SourceId, expected.SourceId) &&
        StringComparer.Ordinal.Equals(current.SourceVersion, expected.SourceVersion) &&
        StringComparer.Ordinal.Equals(current.DefinitionId, expected.DefinitionId) &&
        StringComparer.Ordinal.Equals(current.DefinitionVersionId, expected.DefinitionVersionId) &&
        StringComparer.Ordinal.Equals(current.ArtifactVersion, expected.ArtifactVersion) &&
        current.CreatedAt == expected.CreatedAt &&
        current.PublishedAt == expected.PublishedAt &&
        current.Scope == expected.Scope &&
        current.ExpiresAt == expected.ExpiresAt &&
        StringComparer.Ordinal.Equals(current.ActivationId, expected.ActivationId) &&
        StringComparer.Ordinal.Equals(current.SlotId, expected.SlotId) &&
        StringComparer.Ordinal.Equals(current.TenantId, expected.TenantId);

    public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            var expired = _references.Values
                .Where(reference => reference.IsExpired(now))
                .Select(reference => reference.SourceReferenceId)
                .Order(StringComparer.Ordinal)
                .Take(batch.Limit)
                .ToArray();
            var retired = _references.Values
                .Where(reference => reference.DeletedAt is not null && !reference.IsExpired(now))
                .Select(reference => reference.SourceReferenceId)
                .Order(StringComparer.Ordinal)
                .Take(batch.Limit - expired.Length)
                .ToArray();
            var doomed = expired.Concat(retired).ToArray();

            foreach (var id in doomed)
                _references.Remove(id);

            return ValueTask.FromResult<IReadOnlyCollection<string>>(doomed);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        WorkflowExecutableArtifactCandidateBatch candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        lock (_gate)
        {
            var liveArtifactIds = _references.Values
                .Where(reference => reference.IsLive(now))
                .Select(reference => reference.ArtifactId)
                .ToHashSet(StringComparer.Ordinal);

            return ValueTask.FromResult<IReadOnlyCollection<string>>(
                candidates.ArtifactIds.Where(artifactId => !liveArtifactIds.Contains(artifactId)).ToArray());
        }
    }

    private static RuntimeStorePage<WorkflowExecutableSourceReference> Page(
        RuntimeStorePageRequest query,
        string queryBinding,
        IEnumerable<WorkflowExecutableSourceReference> references)
    {
        var lastId = InMemoryRuntimeStoreContinuation.Decode(query.ContinuationToken, queryBinding, nameof(query.ContinuationToken));
        var ordered = references
            .OrderBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .Where(reference => lastId is null || StringComparer.Ordinal.Compare(reference.SourceReferenceId, lastId) > 0)
            .Take(query.Limit + 1)
            .ToArray();
        var items = ordered.Take(query.Limit).ToArray();
        var nextContinuation = ordered.Length > query.Limit
            ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].SourceReferenceId)
            : null;
        return new RuntimeStorePage<WorkflowExecutableSourceReference>(query, items, nextContinuation);
    }
}
