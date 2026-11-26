using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.History;

/// <summary>Opt-in shell composition for semantic metadata history on eligible new workflow admissions.</summary>
/// <remarks>Compose the runtime and a qualified persistence provider as usual. Disabling new capture leaves retained policies authoritative.</remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Diagnostics")]
[ShellFeature(
    name: "WorkflowsRuntimeHistory",
    DisplayName = "Workflow Runtime History",
    Description = "Records semantic run history with the selected runtime persistence provider. Captures bounded metadata; captured value and payload permissions remain separate.")]
public class WorkflowsRuntimeHistoryFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Capture new runs", Description = "Enables metadata history for eligible new workflow admissions. Existing captured runs keep their pinned policy when disabled.", Category = "Runtime", DefaultValue = "true")]
    public bool Enabled { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddWorkflowRuntimeHistory(new RuntimeHistoryOptions { Enabled = Enabled });
}
