using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Activities.Runtime.Handlers;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Events.Core.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime;

/// <summary>
/// Runtime-side feature for activity construction. Registers the dispatch factory, the
/// descriptor-type → constructor registry, and the Registry + StartUp Task wiring that populates
/// the registry from every contributed <see cref="IActivityConstructor"/>. Carries no Design
/// dependency (Elsa §E2.2).
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesRuntime",
    DisplayName = "Activities Runtime",
    Description = "Activity construction factory and descriptor-type-driven constructor registry."
)]
public class ActivitiesRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Registry — singleton; populated by the startup task below.
        services.AddSingleton<IActivityConstructorRegistry, ActivityConstructorRegistry>();

        // Dispatch factory.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IActivityFactory, ActivityFactory>();
        services.TryAddSingleton<ActivityInputHydrator>();
        services.TryAddSingleton<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
        services.TryAddSingleton<ActivityCompletionProjector>();
        services.TryAddSingleton<ActivityFaultIncidentRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowInvokeActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowParentActivityCompletionSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowResumeBookmarkSchedulerWorkHandler>());

        // Registry + StartUp Task + Domain Event (framework §2.6.1): the startup task publishes the
        // initialization event; the single aggregating handler adds every registered constructor;
        // the task flushes them into the registry. Features contribute by registering an
        // IActivityConstructor.
        services.AddScoped<IStartupTask, ActivityConstructorsStartupTask>();
        services.AddEventHandler<OnActivityConstructorsInitializing, RegisterActivityConstructors>();

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
