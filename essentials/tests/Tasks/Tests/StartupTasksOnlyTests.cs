using CShells;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

/// <summary>
/// The <c>Tasks</c> feature's <see cref="TasksFeature.StartupTasksOnly"/> setting: when set, the shell initializer runs
/// the startup tasks in dependency order through <see cref="ITaskManager.RunStartupTasksOnly"/> and starts no background
/// or recurring task; by default it starts everything, as before.
/// </summary>
[Collection(StartupTaskRuns.Name)]
public sealed class StartupTasksOnlyTests
{
    [Fact]
    public async Task Startup_only_runs_startup_tasks_in_dependency_order()
    {
        var probe = new Probe();
        await using var provider = Compose(new TasksFeature(), probe);

        await provider.GetRequiredService<ITaskManager>().RunStartupTasksOnly(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], probe.StartupOrder);
        await provider.GetRequiredService<ITaskManager>().StopExecutingRegisteredTasks();
    }

    [Fact]
    public async Task With_the_setting_the_initializer_starts_no_background_or_recurring_task()
    {
        var probe = new Probe();
        await using var provider = Compose(new TasksFeature { StartupTasksOnly = true }, probe);

        await ActivatorUtilities.CreateInstance<RunShellTasksInitializer>(provider).InitializeAsync(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], probe.StartupOrder);
        Assert.Equal(0, probe.BackgroundStarts);
        Assert.Equal(0, probe.BackgroundExecutions);
        Assert.Equal(0, probe.RecurringStarts);
        Assert.Equal(0, probe.RecurringSchedules);
        await provider.GetRequiredService<ITaskManager>().StopExecutingRegisteredTasks();
    }

    [Fact]
    public async Task By_default_the_initializer_starts_background_and_recurring_tasks_after_the_startup_tasks()
    {
        var probe = new Probe();
        var feature = new TasksFeature();
        Assert.False(feature.StartupTasksOnly);
        await using var provider = Compose(feature, probe);

        await ActivatorUtilities.CreateInstance<RunShellTasksInitializer>(provider).InitializeAsync(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], probe.StartupOrder);
        Assert.Equal(1, probe.BackgroundStarts);
        Assert.Equal(1, probe.RecurringStarts);
        Assert.Equal(1, probe.RecurringSchedules);
        await provider.GetRequiredService<ITaskManager>().StopExecutingRegisteredTasks();
    }

    private static ServiceProvider Compose(TasksFeature feature, Probe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<Elsa.Locking.Core.IDistributedLockProvider>(new UnusedLockProvider());
        services.AddSingleton(new ShellSettings("default"));
        feature.ConfigureServices(services);
        services.AddSingleton(probe);
        // Registered dependents first, so only the dependency sort can put them in order.
        services.AddScoped<IStartupTask, Third>();
        services.AddScoped<IStartupTask, Second>();
        services.AddScoped<IStartupTask, First>();
        services.AddSingleton<IBackgroundTask, BackgroundProbe>();
        services.AddSingleton<IRecurringTask, RecurringProbe>();
        return services.BuildServiceProvider();
    }

    private sealed class Probe
    {
        public List<string> StartupOrder { get; } = [];
        public int BackgroundStarts;
        public int BackgroundExecutions;
        public int RecurringStarts;
        public int RecurringSchedules;
    }

    private sealed class First(Probe probe) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            probe.StartupOrder.Add("first");
            return Task.CompletedTask;
        }
    }

    [TaskDependency(typeof(First))]
    private sealed class Second(Probe probe) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            probe.StartupOrder.Add("second");
            return Task.CompletedTask;
        }
    }

    [TaskDependency(typeof(Second))]
    private sealed class Third(Probe probe) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            probe.StartupOrder.Add("third");
            return Task.CompletedTask;
        }
    }

    private sealed class BackgroundProbe(Probe probe) : IBackgroundTask
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref probe.BackgroundStarts);
            return Task.CompletedTask;
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref probe.BackgroundExecutions);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecurringProbe(Probe probe) : IRecurringTask
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref probe.RecurringStarts);
            return Task.CompletedTask;
        }

        public ITaskSchedule GetSchedule()
        {
            Interlocked.Increment(ref probe.RecurringSchedules);
            return new NeverSchedule();
        }

        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NeverSchedule : ITaskSchedule
    {
        public IScheduledTaskExecution ScheduleExecution(Func<Task> action) => new NoExecution();
    }

    private sealed class NoExecution : IScheduledTaskExecution
    {
        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
