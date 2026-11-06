using CShells.Features;
using Elsa.Activities.Primitives.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Primitives;

/// <summary>Optional compiler-free release inspection for explicitly supported primitive activity bodies.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ShellFeature(name: "ActivitiesPrimitivesReleaseVerification", DisplayName = "Primitive release verification",
    Description = "Inspects captured WriteLine and WriteLines bodies without executing or importing them.",
    DependsOn = new object[] { "WorkflowReleaseRuntime" })]
public class ActivitiesPrimitivesReleaseVerificationFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, PrimitiveReleaseOccurrenceInspector>());
}
