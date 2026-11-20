using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Publishing.Services;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import.Publishing;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportActivitiesPublishing",
    DisplayName = "Elsa 3 Import Activity Publishing",
    Description = "Publishes imported Elsa 3 reusable activities through the activity publisher, so they run as graph activities.",
    // The import calls this feature's publisher after its commit; the activity publisher is Workflows Publishing's API.
    DependsOn = new object[] { "Elsa3ImportJsonActivities", "WorkflowsPublishingApi" })]
public class Elsa3ImportActivitiesPublishingFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // A replacement contract: exactly one publisher per shell (spec 246 R-1).
        services.Replace(ServiceDescriptor.Scoped<IReusableActivityImportPublisher, PublisherReusableActivityImportPublisher>());
        // The import runs the phase after its commit when a host composes it.
        services.TryAddScoped<ReusableActivityImportPublishPhase>();
    }
}
