using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.SourceCapture;

/// <summary>Optional read-only source bridge. This feature never registers or invokes a compiler.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ShellFeature(name: "WorkflowsPublishingReleaseCapture", DisplayName = "Native published-root release capture",
    Description = "Captures exact authored-version associations and retained native published roots; dependency owners remain separate.",
    DependsOn = new object[] { "WorkflowReleaseCapture", "WorkflowReleaseRuntimeSourceClosure" })]
public class WorkflowsPublishingReleaseCaptureFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseCaptureSource, NativeReleaseCaptureSource>());
    }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        foreach (var contract in new[] { typeof(INativeReleaseCaptureContextAccessor), typeof(INativeReleaseCaptureAuthorization) })
            if (services.Count(item => !item.IsKeyedService && item.ServiceType == contract) != 1)
                throw new InvalidOperationException("Native source capture requires one explicit source namespace and one resource export authorization policy.");
    }
}
