using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryTransientWorkflowExecutableStore(IWorkflowExecutableStore executableStore)
    : ITransientWorkflowExecutableStore
{
    private readonly Dictionary<string, DateTimeOffset> _expirations = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        if (executable.Scope != WorkflowExecutableScope.TransientTestRun)
            throw new ArgumentException("Transient store only accepts transient test-run executables.", nameof(executable));

        if (executable.ExpiresAt is null)
            throw new ArgumentException("Transient test-run executables require an expiration timestamp.", nameof(executable));

        await executableStore.SaveAsync(executable, cancellationToken);

        lock (_gate)
            _expirations[executable.Identity.ArtifactId] = executable.ExpiresAt.Value;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        if (executable?.Scope != WorkflowExecutableScope.TransientTestRun)
            return null;

        if (IsExpired(executable, DateTimeOffset.UtcNow))
        {
            await executableStore.DeleteAsync(artifactId, cancellationToken);
            Forget(artifactId);
            return null;
        }

        Remember(executable);
        return executable;
    }

    public async ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var storedExpiredArtifactIds = (await executableStore.ListAsync(includeTransient: true, cancellationToken: cancellationToken))
            .Where(executable => executable.Scope == WorkflowExecutableScope.TransientTestRun && IsExpired(executable, now))
            .Select(executable => executable.Identity.ArtifactId);

        string[] trackedExpiredArtifactIds;
        lock (_gate)
        {
            trackedExpiredArtifactIds = _expirations
                .Where(item => item.Value <= now)
                .Select(item => item.Key)
                .ToArray();
        }

        var deleted = 0;
        foreach (var artifactId in storedExpiredArtifactIds.Concat(trackedExpiredArtifactIds).Distinct(StringComparer.Ordinal))
        {
            if (await executableStore.DeleteAsync(artifactId, cancellationToken))
                deleted++;

            Forget(artifactId);
        }

        return deleted;
    }

    private bool IsExpired(WorkflowExecutable executable, DateTimeOffset now)
    {
        if (executable.ExpiresAt is { } expiresAt)
            return expiresAt <= now;

        lock (_gate)
            return _expirations.TryGetValue(executable.Identity.ArtifactId, out expiresAt) && expiresAt <= now;
    }

    private void Remember(WorkflowExecutable executable)
    {
        if (executable.ExpiresAt is null)
            return;

        lock (_gate)
            _expirations[executable.Identity.ArtifactId] = executable.ExpiresAt.Value;
    }

    private void Forget(string artifactId)
    {
        lock (_gate)
            _expirations.Remove(artifactId);
    }
}
