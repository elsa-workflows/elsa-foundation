using CShells.Lifecycle;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Elsa.Tasks.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Tasks.Tests;

public sealed class TasksFeatureRegistrationTests
{
    // The shell lifecycle the feature registers: the initializer, and the terminator with its phase and order.
    [Fact]
    public async Task TasksFeature_RegistersShellLifecycleThatStartsAndStopsTasks()
    {
        // The locking feature supplies the lock provider in a real shell.
        var services = TaskServices.Compose(new UnusedLockProvider(), shellName: "t");

        Assert.Contains(services, d => d.ImplementationType == typeof(RunShellTasksInitializer));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(StopShellTasksTerminator) &&
            d.Lifetime == ServiceLifetime.Transient);
        var terminatorRegistration = Assert.Single(services
            .Where(d => d.ServiceType == typeof(ShellTerminatorRegistration))
            .Select(d => d.ImplementationInstance)
            .OfType<ShellTerminatorRegistration>(),
            x => x.TerminatorType == typeof(StopShellTasksTerminator));
        Assert.Equal(LifecyclePhase.Start, terminatorRegistration.Phase);
        Assert.Equal(0, terminatorRegistration.Order);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ITaskManager>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TaskExecutor>());
    }
}
