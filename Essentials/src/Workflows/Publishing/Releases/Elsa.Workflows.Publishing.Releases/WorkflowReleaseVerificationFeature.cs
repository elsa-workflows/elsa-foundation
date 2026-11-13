using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases;

/// <summary>Opt-in semantic orchestration. Resolving IReleaseSemanticVerifier validates exact-key adapter composition; no owner adapter is installed here.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowReleaseVerification", DisplayName = "Workflow release verification", Description = "Exact owner-codec dispatch and complete actual-fact comparison; owner adapters must be installed separately.",
    DependsOn = new object[] { "WorkflowReleaseTransport" })]
public class WorkflowReleaseVerificationFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<ReleaseSemanticRegistry>();
        services.TryAddScoped<IReleaseSemanticVerifier, ReleaseSemanticVerifier>();
    }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(item => !item.IsKeyedService && item.ServiceType == typeof(IReleaseSemanticVerifier)) != 1)
            throw new InvalidOperationException("Exactly one canonical IReleaseSemanticVerifier registration must be selected.");
        if (services.Count(item => !item.IsKeyedService && item.ServiceType == typeof(IReleaseBundleCodec)) != 1)
            throw new InvalidOperationException("Release semantic verification requires exactly one canonical IReleaseBundleCodec registration.");
    }
}
