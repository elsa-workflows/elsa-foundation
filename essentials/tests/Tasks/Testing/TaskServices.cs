using CShells;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Testing;

/// <summary>A shell's task services as the public <see cref="TasksFeature"/> composes them, over the locks a test supplies.</summary>
public static class TaskServices
{
    /// <summary>
    /// The services of a shell named <paramref name="shellName"/>: <see cref="TasksFeature"/> over
    /// <paramref name="locks"/>, logging to nowhere. Callers add their own tasks and services before building.
    /// </summary>
    public static ServiceCollection Compose(IDistributedLockProvider locks, string shellName = "default")
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(locks);
        services.AddSingleton(new ShellSettings(shellName));
        new TasksFeature().ConfigureServices(services);
        return services;
    }

    /// <summary>The executor such a shell runs its tasks through.</summary>
    public static ITaskExecutor Executor(IDistributedLockProvider locks, string shellName = "default") =>
        Compose(locks, shellName).BuildServiceProvider().GetRequiredService<ITaskExecutor>();
}
