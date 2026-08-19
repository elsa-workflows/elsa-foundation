using CShells.Features;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Internal.Behaviors;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesBpmnRuntime",
    DisplayName = "Activities BPMN (Runtime)",
    Description = "BPMN 2.0 process composite execution: token coordination, element behaviors and start-trigger projection."
)]
public class ActivitiesBpmnRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BpmnTokenCoordinator>();
        services.AddSingleton<BpmnStatePersister>();
        services.AddSingleton<BpmnExecutionEngine>();
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.None());
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.Timer());
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.Message());
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.Signal());
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.Escalation());
        services.AddSingleton<IBpmnElementBehavior>(StartEventBehavior.Error());
        services.AddSingleton<IBpmnElementBehavior, NoneEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, TerminateEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, CatchEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, CompensationThrowEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, CompensationEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, CancelEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, EscalationThrowEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, EscalationEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, MessageThrowEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, MessageEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, TaskBehavior>();
        services.AddSingleton<IBpmnElementBehavior, SubProcessBehavior>();
        services.AddSingleton<IBpmnElementBehavior, ExclusiveGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, ParallelGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, InclusiveGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, EventBasedGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, BoundaryEventBehavior>();
        services.AddSingleton<IBpmnBehaviorRegistry, BpmnBehaviorRegistry>();

        // Publish-time start-trigger surface (spec 117): the process node registers one trigger binding per
        // event-defined start element, and — for timer starts — one recurring schedule per element.
        services.AddSingleton<IActivityTriggerStimulusProvider, BpmnProcessTriggerStimulusProvider>();
        services.AddSingleton<IRecurringTriggerScheduleProvider, BpmnProcessRecurringScheduleProvider>();
    }
}
