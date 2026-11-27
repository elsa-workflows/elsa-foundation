using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflow Runtime Attention for retained workflows",
    Description = "Keeps workflow attention visible without the legacy runner and reports when exact workflow signals are unavailable.",
    DependsOn = new object[] { "AttentionApi" })]
public sealed class WorkflowsRuntimeRetainedAttentionFeature : IShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeRetainedAttention";

    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowRuntimeAttentionQuery, UnavailableWorkflowRuntimeAttentionQuery>();
        WorkflowsRuntimeAttentionFeature.AddContributor(services);
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(service => !service.IsKeyedService && service.ServiceType == typeof(LegacyWorkflowRuntimeAttentionSelected)))
            throw new InvalidOperationException($"Select either {FeatureName} or WorkflowsRuntimeAttention because they supply the same attention contributor.");
    }
}
