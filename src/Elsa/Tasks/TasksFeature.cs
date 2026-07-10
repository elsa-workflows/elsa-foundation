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
        // Shell-singleton lifecycle: the task manager and its executor own the start/stop of
        // shell-lifetime background and recurring tasks (and the singleton IEventChannel drained by
        // BackgroundEventPublisher). They must therefore live for the shell's lifetime — a scoped
        // manager is disposed at the end of the shell-initializer scope, and its DisposeAsync would
        // then tear those singletons down (cancelling the tenant token and completing the event
        // channel writer) seconds after activation. TaskExecutor depends only on the singleton
        // IDistributedLockProvider, so singleton is scope-safe. Scoped IStartupTasks are still run in
        // a dedicated scope created inside TaskManager (see RunStartupTasks).
        services.AddSingleton<TaskExecutor>();
        // Stays scoped deliberately: it is only ever resolved inside the dedicated scope
        // TaskManager.RunStartupTasks opens for the (scoped) startup tasks, never from the root path
        // that starts background/recurring tasks — so it must not be promoted to singleton here.
        services.AddScoped<ITopologicalTaskSorter, TopologicalTaskSorter>();
        services.AddSingleton<IBackgroundTaskStarter>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<ITaskExecutor>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<ITaskManager, TaskManager>();
        services.AddShellInitializer<RunShellTasksInitializer>(LifecyclePhase.Start, 0);
        services.AddShellTerminator<StopShellTasksTerminator>(LifecyclePhase.Start, 0);
    }
}
