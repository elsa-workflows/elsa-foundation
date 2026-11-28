using CShells.Lifecycle;
using Elsa.Tasks.Core;
using Elsa.Tasks.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Tasks.Services;

internal sealed class RunShellTasksInitializer(ITaskManager taskManager, IOptions<TaskStartOptions> options) : IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (options.Value.StartupTasksOnly)
            await taskManager.RunStartupTasksOnly(cancellationToken);
        else
            await taskManager.StartExecutingRegisteredTasks(cancellationToken);
    }
}
