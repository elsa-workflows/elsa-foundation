namespace Elsa.Tasks.Core;

public interface ITaskManager
{
    Task StartExecutingRegisteredTasks(CancellationToken token);

    /// <summary>
    /// Stops the tasks owned by the current shell generation. The shell's terminator calls it before the shell provider is
    /// disposed.
    /// </summary>
    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
