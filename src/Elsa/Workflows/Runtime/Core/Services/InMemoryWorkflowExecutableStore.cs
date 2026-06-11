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

    public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
            return ValueTask.FromResult(_executables.GetValueOrDefault(artifactId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutable>>(_executables.Values.ToArray());
    }
}
