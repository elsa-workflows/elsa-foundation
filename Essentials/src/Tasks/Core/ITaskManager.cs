namespace Elsa.Tasks.Core;

public interface ITaskManager
{
    /// <summary>
    /// Runs the shell's startup tasks in order, then starts its background and recurring tasks. The shell's initializer
    /// calls it when the shell activates.
    /// </summary>
    /// <remarks>
    /// The caller token cancels startup, not the lifetime of successfully started tasks. Background and recurring
    /// execution belongs to the shell until <see cref="StopExecutingRegisteredTasks"/> or provider disposal.
    /// </remarks>
    Task StartExecutingRegisteredTasks(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the tasks owned by the current shell generation. The shell's terminator calls it before the shell provider is
    /// disposed.
    /// </summary>
    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
