using CShells.Features;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesBpmn",
    DisplayName = "Activities BPMN",
    Description = "BPMN 2.0 publication trigger and recurring schedule metadata."
)]
public class ActivitiesBpmnFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Publish-time start-trigger surface: the process node registers one trigger binding per
        // event-defined start element, and — for timer starts — one recurring schedule per element.
        services.AddSingleton<IActivityTriggerStimulusProvider, BpmnProcessTriggerStimulusProvider>();
        services.AddSingleton<IRecurringTriggerScheduleProvider, BpmnProcessRecurringScheduleProvider>();
    }
}
