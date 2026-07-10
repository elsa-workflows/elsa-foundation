namespace Elsa.Tasks.Core;

public interface ITaskManager
{
    Task StartExecutingRegisteredTasks(CancellationToken token);

    Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default);
}
