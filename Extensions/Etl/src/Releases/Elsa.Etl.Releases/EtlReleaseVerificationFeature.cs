using CShells.Features;
using Elsa.Etl.Releases.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Releases;

/// <summary>Opt-in compiler-free ETL release inspection. It installs no ETL worker, catalog or admission authority.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ShellFeature(name: "EtlReleaseVerification", DisplayName = "ETL release verification", Description = "Retained ETL v1/v2 content validation and scoped binding requirements.",
    DependsOn = new object[] { "WorkflowReleaseVerification" })]
public class EtlReleaseVerificationFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IEtlReleaseArtifactCodec, EtlReleaseArtifactCodec>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseArtifactSemanticVerifier, EtlReleaseSemanticVerifier>());
    }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(item => !item.IsKeyedService && item.ServiceType == typeof(IEtlReleaseArtifactCodec)) != 1)
            throw new InvalidOperationException("Exactly one canonical IEtlReleaseArtifactCodec registration must be selected.");
    }
}
