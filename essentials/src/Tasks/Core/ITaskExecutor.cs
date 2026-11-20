namespace Elsa.Tasks.Core;

public interface ITaskExecutor
{
    /// <summary>
    /// Executes the specified task.
    /// </summary>
    /// <param name="task"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ExecuteTaskAsync(ITask task, CancellationToken cancellationToken);
}