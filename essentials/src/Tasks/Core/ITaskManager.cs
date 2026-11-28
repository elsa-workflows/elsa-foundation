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
    /// Runs the shell's startup tasks in their dependency order and starts no background or recurring task. The shell's
    /// initializer calls it instead of <see cref="StartExecutingRegisteredTasks"/> when the <c>Tasks</c> feature's
    /// <c>StartupTasksOnly</c> setting is set, for a process that needs the startup work but no shell-lifetime tasks.
    /// </summary>
    Task RunStartupTasksOnly(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the tasks owned by the current shell generation. The shell's terminator calls it before the shell provider is
    /// disposed.
    /// </summary>
    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
