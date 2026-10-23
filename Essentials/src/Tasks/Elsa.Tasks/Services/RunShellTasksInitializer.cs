using CShells.Lifecycle;
using Elsa.Tasks.Core;

namespace Elsa.Tasks.Services;

internal sealed class RunShellTasksInitializer(ITaskManager taskManager) : IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await taskManager.StartExecutingRegisteredTasks(cancellationToken);
    }
}
