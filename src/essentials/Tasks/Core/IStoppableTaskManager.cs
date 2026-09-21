namespace Elsa.Tasks.Core;

/// <summary>
/// Optional task-manager capability for stopping shell-lifetime tasks before the shell provider is disposed.
/// </summary>
public interface IStoppableTaskManager
{
    /// <summary>Stops the tasks owned by the current shell generation.</summary>
    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
