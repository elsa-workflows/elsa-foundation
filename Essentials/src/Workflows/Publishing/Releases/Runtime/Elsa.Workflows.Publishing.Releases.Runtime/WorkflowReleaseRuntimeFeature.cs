using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Opt-in Runtime artifact profile. Opaque activity/model/provider inspectors are separate mandatory owner capabilities.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowReleaseRuntime", DisplayName = "Workflow release Runtime bridge", Description = "Compiler-free Runtime artifact codecs, exact identities and mandatory owner inspection sites; no staging or serving authority.",
    DependsOn = new object[] { "WorkflowReleaseVerification" })]
public class WorkflowReleaseRuntimeFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IReleaseRuntimeArtifactCodec, ReleaseRuntimeArtifactCodec>();
        services.TryAddScoped<RuntimeCapturedContentCache>();
        services.TryAddScoped<IReleaseRuntimeSiteReader, ReleaseRuntimeSiteReader>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseArtifactSemanticVerifier, ReleaseRuntimeArtifactVerifier>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, RuntimeIntrinsicReleaseInspector>());
    }
    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(x => !x.IsKeyedService && x.ServiceType == typeof(IReleaseRuntimeArtifactCodec)) != 1 ||
            services.Count(x => !x.IsKeyedService && x.ServiceType == typeof(IReleaseRuntimeSiteReader)) != 1)
            throw new InvalidOperationException("Exactly one Runtime release artifact codec and captured-site reader must be selected.");
        if (services.Count(x => !x.IsKeyedService && x.ServiceType == typeof(IWorkflowExecutableHasher)) != 1)
            throw new InvalidOperationException("The Runtime release profile requires exactly one Runtime-owned IWorkflowExecutableHasher.");
    }
}
