namespace Elsa.Tasks.Core
{
    public interface ITaskManager
    {
        Task StartExecutingRegisteredTasks(CancellationToken token);
    }
}
