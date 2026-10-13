using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Tasks.Tests;

/// <summary>
/// The task manager runs startup tasks in <c>[TaskDependency]</c> order, whatever order they were registered in. Without
/// the dependency sort they would run in registration order and nothing would fail, so this runs them through the
/// manager the feature composes.
/// </summary>
[Collection(StartupTaskRuns.Name)]
public sealed class StartupTaskOrderingTests
{
    [Fact]
    public async Task Startup_tasks_run_after_the_tasks_they_depend_on()
    {
        var order = new List<string>();
        var services = TaskServices.Compose(new UnusedLockProvider());
        services.AddSingleton(order);
        // Registered dependents first, so only the dependency sort can put them in order.
        services.AddScoped<IStartupTask, Third>();
        services.AddScoped<IStartupTask, Second>();
        services.AddScoped<IStartupTask, First>();
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITaskManager>().StartExecutingRegisteredTasks(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], order);
    }

    private sealed class First(List<string> order) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            order.Add("first");
            return Task.CompletedTask;
        }
    }

    [TaskDependency(typeof(First))]
    private sealed class Second(List<string> order) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            order.Add("second");
            return Task.CompletedTask;
        }
    }

    [TaskDependency(typeof(Second))]
    private sealed class Third(List<string> order) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            order.Add("third");
            return Task.CompletedTask;
        }
    }
}
