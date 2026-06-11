using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class TasksFeatureRegistrationTests
{
    [Fact]
    public async Task TasksFeature_RegistersShellInitializerThatStartsTasks()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        new TasksFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ImplementationType == typeof(RunShellTasksInitializer));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ITaskManager>());
    }
}
