using CShells;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>The executor a shell runs its tasks through, as <see cref="TasksFeature"/> composes it, over the given locks.</summary>
internal static class ComposedTaskExecutor
{
    public static ITaskExecutor Over(IDistributedLockProvider locks)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(locks);
        services.AddSingleton(new ShellSettings("default"));
        new TasksFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<ITaskExecutor>();
    }
}
