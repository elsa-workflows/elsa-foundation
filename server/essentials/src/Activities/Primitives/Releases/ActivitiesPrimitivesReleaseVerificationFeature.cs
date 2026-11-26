using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Primitives.Releases;

/// <summary>Optional compiler-free release inspection for the explicitly supported WriteLine activity profile.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ShellFeature(name: "ActivitiesPrimitivesReleaseVerification", DisplayName = "Primitive release verification",
    Description = "Inspects captured WriteLine bodies without executing or importing them.",
    DependsOn = new object[] { "WorkflowReleaseRuntime", "ActivitiesPrimitives" })]
public class ActivitiesPrimitivesReleaseVerificationFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, PrimitiveReleaseOccurrenceInspector>());
}
