using CShells.Lifecycle;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class TasksFeatureRegistrationTests
{
    [Fact]
    public async Task TasksFeature_RegistersShellLifecycleThatStartsAndStopsTasks()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        new TasksFeature().ConfigureServices(services);

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
    }
}
