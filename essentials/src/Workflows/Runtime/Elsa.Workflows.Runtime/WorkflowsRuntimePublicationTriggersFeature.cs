using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>Provides extraction and indexing of trigger bindings for workflow publications.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Triggers")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflow Publication Triggers",
    Description = "Extracts and indexes trigger bindings for published workflows.")]
public sealed class WorkflowsRuntimePublicationTriggersFeature : IShellFeature
{
    public const string FeatureName = "WorkflowsRuntimePublicationTriggers";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWorkflowPublicationTriggers();
    }
}
