using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Shared surface of every Groundwork persistence shell feature: the bounded, shell-local
/// workflow-executable cache settings that all variants expose with identical semantics. Provider
/// and target selection stay in the derived features; this base only removes the per-feature copies
/// of the settings whose wording and defaults must not drift apart.
/// </summary>
public abstract class GroundworkPersistenceShellFeatureBase : IShellFeature
{
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

    public abstract void ConfigureServices(IServiceCollection services);

    protected WorkflowExecutableCacheOptions CreateWorkflowExecutableCacheOptions() => new()
    {
        Enabled = CacheWorkflowExecutables,
        Capacity = WorkflowExecutableCacheCapacity
    };

    protected static string ValueOrDefault(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
