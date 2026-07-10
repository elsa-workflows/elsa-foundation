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

    public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>(
                _references.Values.Where(reference => string.Equals(reference.ArtifactId, artifactId, StringComparison.Ordinal)).ToArray());
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(
        WorkflowExecutableReferenceScope? scope = null,
        bool liveOnly = false,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = now ?? DateTimeOffset.UtcNow;

        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>(_references.Values
                .Where(reference => scope is null || reference.Scope == scope)
                .Where(reference => !liveOnly || reference.IsLive(asOf))
                .ToArray());
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

    public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var doomed = _references.Values
                .Where(reference => reference.DeletedAt is not null || reference.IsExpired(now))
                .Select(reference => reference.SourceReferenceId)
                .ToArray();

            foreach (var id in doomed)
                _references.Remove(id);

            return ValueTask.FromResult<IReadOnlyCollection<string>>(doomed);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        IEnumerable<string> artifactIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactIds);
        var candidates = artifactIds.Distinct(StringComparer.Ordinal).ToArray();

        lock (_gate)
        {
            var liveArtifactIds = _references.Values
                .Where(reference => reference.IsLive(now))
                .Select(reference => reference.ArtifactId)
                .ToHashSet(StringComparer.Ordinal);

            return ValueTask.FromResult<IReadOnlyCollection<string>>(
                candidates.Where(artifactId => !liveArtifactIds.Contains(artifactId)).ToArray());
        }
    }
}
