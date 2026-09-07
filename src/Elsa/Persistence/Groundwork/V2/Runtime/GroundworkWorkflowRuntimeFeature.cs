using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Backs the complete workflow-runtime persistence family with the public Groundwork API.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkWorkflowRuntime",
    DisplayName = "Groundwork Workflow Runtime",
    Description = "Persists workflow runtime state through the public Groundwork API on a selected provider target.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class GroundworkWorkflowRuntimeFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding runtime state. Defaults to 'default'.",
        Category = "Persistence")]
    public string? Target { get; set; }

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded shell-local cache of immutable workflow executable artifacts loaded from durable storage, isolated by persistence scope.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

    [ManifestSetting(
        DisplayName = "Recovery continuation signing key",
        Description = "At least 32 UTF-8 bytes shared by nodes that consume durable recovery pages. Required for durable recovery paging.",
        Category = "Security",
        Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<RuntimeRecoveryContinuationOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(RecoveryContinuationSigningKey))
                options.SigningKey = RecoveryContinuationSigningKey;
            options.AllowEphemeralDevelopmentKey = false;
        });
        services.AddGroundworkV2RuntimeStores(
            new WorkflowExecutableCacheOptions
            {
                Enabled = CacheWorkflowExecutables,
                Capacity = WorkflowExecutableCacheCapacity
            },
            Target);
    }
}
