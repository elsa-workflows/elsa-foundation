using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Burst-cached <see cref="IWorkflowExecutableReader"/> (ADR 0031 item b, spec 111). When a burst owns the current
/// drain (<see cref="IWorkflowBurstScopeAccessor.Current"/> is non-null) the pinned executable is served once per
/// <c>ArtifactId</c> from the burst scope; every later drain-path read of the same immutable artifact is a memory hit.
/// When no burst is active — no router established one, the kill switch is off, or this is a non-drain caller — the read
/// falls straight through to the durable store, so the burst-absent path is byte-identical.
/// </summary>
public sealed class BurstCachedWorkflowExecutableReader(
    IWorkflowExecutableStore executableStore,
    IWorkflowBurstScopeAccessor burstScopeAccessor) : IWorkflowExecutableReader
{
    // Namespaced key so a future consumer that caches other objects under the same burst scope cannot collide.
    private static string ExecutableKey(string artifactId) => $"executable:{artifactId}";

    public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var scope = burstScopeAccessor.Current;
        if (scope is null)
            return executableStore.FindAsync(artifactId, cancellationToken);

        return scope.GetOrAddAsync(
            ExecutableKey(artifactId),
            ct => executableStore.FindAsync(artifactId, ct),
            cancellationToken);
    }
}
