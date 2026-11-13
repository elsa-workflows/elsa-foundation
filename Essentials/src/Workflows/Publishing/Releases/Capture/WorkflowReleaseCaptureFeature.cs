using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases.Capture;

/// <summary>Optional complete source capture; selected source adapters and archive persistence are host-owned.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowReleaseCapture", DisplayName = "Workflow release capture", Description = "Retained owner acquisition and complete verified archive creation.",
    DependsOn = new object[] { "WorkflowReleaseVerification" })]
public class WorkflowReleaseCaptureFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services) => services.TryAddScoped<IReleaseCaptureService, ReleaseCaptureService>();
    public virtual void PostConfigureServices(IServiceCollection services)
    {
        foreach (var contract in new[] { typeof(IReleaseCaptureService), typeof(IReleaseBundleBuilder), typeof(IReleaseBundleCodec), typeof(IReleaseArchiveWriter), typeof(IReleaseArchiveStoreCapabilities) })
            if (services.Count(item => !item.IsKeyedService && item.ServiceType == contract) != 1)
                throw new InvalidOperationException("Release capture requires one selected implementation of each capture, builder, codec and archive contract, including store capabilities.");
    }
}
