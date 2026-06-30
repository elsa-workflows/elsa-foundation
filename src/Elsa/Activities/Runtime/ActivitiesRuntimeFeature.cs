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
        services.TryAddSingleton<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
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

        // Startup pass (FR-004b / research D8 revised): register the CLR types reachable through framework
        // activity inputs/outputs under the shared TypeAliasConvention, so the alias the reflection-only CLR
        // scanner emits for a complex- or enum-typed input resolves back to its real CLR type at compile time
        // instead of falling back to object.
        services.AddScoped<IStartupTask, RegisterActivityIoTypesStartupTask>();
    }
}
