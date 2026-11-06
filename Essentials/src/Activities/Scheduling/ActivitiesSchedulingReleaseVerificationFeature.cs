using CShells.Features;
using Elsa.Activities.Scheduling.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling;

/// <summary>Optional captured-body inspection for the supported Delay continuation contract.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ShellFeature(name: "ActivitiesSchedulingReleaseVerification", DisplayName = "Scheduling release verification",
    Description = "Inspects captured Delay bodies without scheduling timers or starting Runtime.",
    DependsOn = new object[] { "WorkflowReleaseRuntime" })]
public class ActivitiesSchedulingReleaseVerificationFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, DelayReleaseOccurrenceInspector>());
}
