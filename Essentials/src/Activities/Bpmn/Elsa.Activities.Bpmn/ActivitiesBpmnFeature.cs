using CShells.Features;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CShells.Lifecycle;

namespace Elsa.Activities.Bpmn;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesBpmn",
    DisplayName = "Activities BPMN",
    Description = "BPMN 2.0 process composite activity and executable element-graph contracts."
)]
public class ActivitiesBpmnFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, BpmnStructureHandler>();
        services.AddScoped<BpmnStateCodec>();
        services.AddScoped<BpmnWorkLedger>();
        services.TryAddScoped<IBpmnProcessRuntime, BpmnProcessRuntime>();
        services.AddSingleton<IShellInitializer>(new BpmnRuntimeRegistrationInitializer(services));
        // Publish-time start-trigger surface: the process node registers one trigger binding per
        // event-defined start element, and — for timer starts — one recurring schedule per element.
        services.AddSingleton<IActivityTriggerStimulusProvider, BpmnProcessTriggerStimulusProvider>();
        services.AddSingleton<IRecurringTriggerScheduleProvider, BpmnProcessRecurringScheduleProvider>();
    }
}
