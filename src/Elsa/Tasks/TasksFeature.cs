using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using CShells.Lifecycle;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Tasks;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Tasks")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "Tasks",
    DisplayName = "Tasks",
    Description = "Provides services to enable and configure tasks (e.g. background- and recurring tasks). To register tasks yourself, implement the appropriate task interfaces and register them in the DI container (e.g. IRecurringTask, IStartupTask, or IBackgroundTask)."
)]
public class TasksFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<TaskExecutor>();
        services.AddScoped<ITopologicalTaskSorter, TopologicalTaskSorter>();
        services.AddScoped<IBackgroundTaskStarter>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddScoped<ITaskExecutor>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddScoped<ITaskManager, TaskManager>();
        services.AddShellInitializer<RunShellTasksInitializer>(LifecyclePhase.Start, 0);
    }
}
