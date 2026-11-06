using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases;

/// <summary>Opt-in transport parsing only; grants no owner import, publication or start capability.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowReleaseTransport", DisplayName = "Workflow release transport", Description = "Bounded compiler-free release transport and declared graph verification; owner semantic validation remains required.")]
public class WorkflowReleaseTransportFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services) => services.TryAddScoped<IReleaseBundleCodec, ReleaseBundleCodec>();

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(item => !item.IsKeyedService && item.ServiceType == typeof(IReleaseBundleCodec)) != 1)
            throw new InvalidOperationException("Exactly one canonical IReleaseBundleCodec registration must be selected.");
    }
}
