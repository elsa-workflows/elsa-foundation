using CShells;
using Elsa.Locking.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

/// <summary>A shell's services as <see cref="TasksFeature"/> composes them, over the given locks, logging to nowhere.</summary>
internal static class TaskServices
{
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
}
