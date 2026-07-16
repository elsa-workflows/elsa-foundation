using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime;

/// <summary>
/// Runtime-side feature for durable activity invocation, completion projection, and CLR type discovery.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesRuntime",
    DisplayName = "Activities Runtime",
    Description = "Typed activity invocation and completion runtime."
)]
public class ActivitiesRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ActivityInputHydrator>();
        services.TryAddSingleton<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
        services.TryAddSingleton<ActivityCompletionProjector>();
        services.TryAddSingleton<ActivityFaultIncidentRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowInvokeActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowParentActivityCompletionSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowResumeBookmarkSchedulerWorkHandler>());

        // Startup pass (FR-004b / research D8 revised): register the activity CLR types — the activity types
        // themselves AND their input/output element types — under the shared TypeAliasConvention. This lets the
        // CLR construction descriptor resolve an activity's stable alias back to its real type (no
        // Assembly.Load(name, version)), and a complex- or enum-typed input resolve to its real CLR type at
        // compile time instead of falling back to object. Sources both the runtime-loaded assemblies and the
        // registered IFeatureAssemblyProvider set, so dynamically-loaded extension-builder activities are covered
        // once their package is loaded; the pass re-runs on each shell (re)build.
        services.AddScoped<IStartupTask, RegisterActivityTypesStartupTask>();
    }
}
