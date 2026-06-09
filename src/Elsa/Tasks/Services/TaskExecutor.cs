using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using System.Reflection;

namespace Elsa.Tasks.Services
{
    public sealed class TaskExecutor(IDistributedLockProvider distributedLockProvider) : ITaskExecutor, IBackgroundTaskStarter
    {
        public async Task ExecuteTaskAsync(ITask task, CancellationToken cancellationToken)
        {
            await ExecuteInternalAsync(task, () => task.ExecuteAsync(cancellationToken), cancellationToken);
        }

        public async Task StartAsync(IBackgroundTask task, CancellationToken cancellationToken)
        {
            await ExecuteInternalAsync(task, () => task.StartAsync(cancellationToken), cancellationToken);
        }

        public async Task StopAsync(IBackgroundTask task, CancellationToken cancellationToken)
        {
            await ExecuteInternalAsync(task, () => task.StopAsync(cancellationToken), cancellationToken);
        }

        private async Task ExecuteInternalAsync(ITask task, Func<Task> action, CancellationToken cancellationToken)
        {
            var taskType = task.GetType();
            var singleNodeTask = taskType.GetCustomAttribute<SingleNodeTaskAttribute>() != null;

            if (singleNodeTask)
            {
                var resourceName = taskType.AssemblyQualifiedName!;
                await using (await distributedLockProvider.AcquireLockAsync(resourceName, cancellationToken: cancellationToken))
                {
                    await action();
                }
            }
            else
            {
                await action();
            }
        }
    }
}
