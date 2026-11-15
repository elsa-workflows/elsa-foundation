using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.SourceCapture;

/// <summary>Optional original reusable-catalog acquisition; no compiler, staging or visibility grant.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ShellFeature(name: "WorkflowsPublishingReleaseCatalogCapture", DisplayName = "Native release catalog capture",
    Description = "Copies exact original Activity Definition publication evidence reached by retained Runtime containers.",
    DependsOn = new object[] { "WorkflowsPublishingReleaseCapture" })]
public class WorkflowsPublishingReleaseCatalogCaptureFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseSourceMemberContributor, NativeCatalogSourceMemberContributor>());
    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(value => !value.IsKeyedService && value.ServiceType == typeof(INativeReleaseCatalogCaptureAuthorization)) != 1)
            throw new InvalidOperationException("Native catalog capture requires exactly one explicit original-publication export policy.");
    }
}
