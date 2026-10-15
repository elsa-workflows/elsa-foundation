namespace Elsa.Tasks.Core;

public interface ITaskManager
{
    /// <summary>
    /// Runs the shell's startup tasks in order, then starts its background and recurring tasks. The shell's initializer
    /// calls it when the shell activates.
    /// </summary>
    Task StartExecutingRegisteredTasks(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the tasks owned by the current shell generation. The shell's terminator calls it before the shell provider is
    /// disposed.
    /// </summary>
    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
