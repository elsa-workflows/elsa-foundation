using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutableStore : IWorkflowExecutableStore
{
    private readonly Dictionary<string, WorkflowExecutable> _executables = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        lock (_gate)
            _executables[executable.Identity.ArtifactId] = executable;

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> SoftDeleteAsync(string artifactId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
        {
            if (!_executables.TryGetValue(artifactId, out var executable))
                return ValueTask.FromResult(false);

            _executables[artifactId] = executable.WithDeleted(deletedAt, reason);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> RestoreAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
        {
            if (!_executables.TryGetValue(artifactId, out var executable))
                return ValueTask.FromResult(false);

            _executables[artifactId] = executable.WithRestored();
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
            return ValueTask.FromResult(_executables.Remove(artifactId));
    }

    public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
        {
            var executable = _executables.GetValueOrDefault(artifactId);
            return ValueTask.FromResult(executable?.DeletedAt is null || includeDeleted ? executable : null);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(
        bool includeTransient = false,
        CancellationToken cancellationToken = default,
        bool includeDeleted = false)
    {
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutable>>(_executables.Values
                .Where(executable => includeTransient || executable.Scope == WorkflowExecutableScope.Published)
                .Where(executable => includeDeleted || executable.DeletedAt is null)
                .ToArray());
    }
}
