namespace Elsa.Tasks.Core
{
    public interface ITask
    {
        Task ExecuteAsync(CancellationToken cancellationToken);
    }
}