using CShells;
using CShells.Lifecycle;
using Elsa.Tasks.Core;

namespace Elsa.Server.Shells;

internal sealed class ShellRegisteredTaskRunnerHostedService(IShellRegistry registry) : IHostedService
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry, nameof(registry));
    private readonly ShellRegisteredTaskRunner _taskRunner = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Subscribe(_taskRunner);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registry.Unsubscribe(_taskRunner);
        return Task.CompletedTask;
    }
}

internal sealed class ShellRegisteredTaskRunner : IShellLifecycleSubscriber
{
    public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
    {
        if (current != ShellLifecycleState.Active)
        {
            return Task.CompletedTask;
        }

        var taskManager = shell.ServiceProvider.GetService<ITaskManager>();
        if (taskManager is null)
        {
            return Task.CompletedTask;
        }

        return taskManager.StartExecutingRegisteredTasks(cancellationToken);
    }
}