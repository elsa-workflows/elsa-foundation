using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.History;

/// <summary>Production metadata history; captured values retain their independent policy and payload boundary.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "WorkflowsRuntimeHistory", DisplayName = "Run history",
    Description = "Captures chronological runtime metadata with each existing checkpoint and supports run and activity journals.",
    DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public class WorkflowsRuntimeHistoryFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Capture new runs", Category = "Runtime", DefaultValue = "true",
        Description = "Applies to new admissions. Existing runs retain their pinned capture policy.")]
    public bool Enabled { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddRuntimeHistory(options => options.Enabled = Enabled);
}
