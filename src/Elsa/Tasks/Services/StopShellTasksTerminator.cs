using CShells.Lifecycle;
using Elsa.Tasks.Core;

namespace Elsa.Tasks.Services;

/// <summary>Stops shell-lifetime tasks while their services are still available.</summary>
public sealed class StopShellTasksTerminator(ITaskManager taskManager) : IShellTerminator
{
    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default) =>
        taskManager.StopExecutingRegisteredTasks(cancellationToken);
}
