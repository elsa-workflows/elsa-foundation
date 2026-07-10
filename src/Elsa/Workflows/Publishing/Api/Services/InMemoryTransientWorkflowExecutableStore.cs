using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Transitional facade over the single content-addressed artifact store for expiring test-run executables
/// (ADR 0040). The artifact no longer carries scope/expiry (those are reference facts), so this shim tracks the
/// per-artifact expiry alongside and sweeps expired artifacts from the shared store. The reference-driven test-run
/// rewiring (an expiring TestRun-scope source reference) is worker W3's slice.
/// </summary>
public sealed class InMemoryTransientWorkflowExecutableStore(IWorkflowExecutableStore executableStore)
    : ITransientWorkflowExecutableStore
{
    private readonly Dictionary<string, DateTimeOffset> _expirations = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public async ValueTask SaveAsync(WorkflowExecutable executable, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        await executableStore.SaveAsync(executable, cancellationToken);

        lock (_gate)
            _expirations[executable.Identity.ArtifactId] = expiresAt;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        if (executable is null)
            return null;

        // Only artifacts this facade minted (tracked in _expirations) are visible through it; a durable published
        // artifact sharing the id is not a test-run artifact.
        if (!TryGetExpiry(artifactId, out var expiresAt))
            return null;

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            await executableStore.DeleteAsync(artifactId, cancellationToken);
            Forget(artifactId);
            return null;
        }

        return executable;
    }

    public async ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        string[] expiredArtifactIds;
        lock (_gate)
        {
            expiredArtifactIds = _expirations
                .Where(item => item.Value <= now)
                .Select(item => item.Key)
                .ToArray();
        }

        var deleted = 0;
        foreach (var artifactId in expiredArtifactIds)
        {
            if (await executableStore.DeleteAsync(artifactId, cancellationToken))
                deleted++;

            Forget(artifactId);
        }

        return deleted;
    }

    private bool TryGetExpiry(string artifactId, out DateTimeOffset expiresAt)
    {
        lock (_gate)
            return _expirations.TryGetValue(artifactId, out expiresAt);
    }

    private void Forget(string artifactId)
    {
        lock (_gate)
            _expirations.Remove(artifactId);
    }
}
