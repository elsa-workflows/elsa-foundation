using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Shared settings and shell-context plumbing of the per-provider Groundwork unified persistence
/// features. Lives here rather than in <c>Elsa.Persistence.Groundwork.Unified</c> because that
/// project deliberately keeps a single project reference; every provider's Unified project already
/// sees this project through its provider reference.
/// </summary>
public abstract class GroundworkUnifiedPersistenceShellFeatureBase : GroundworkPersistenceShellFeatureBase
{
    protected GroundworkUnifiedPersistenceShellFeatureBase(ShellFeatureContext context) =>
        Context = context ?? throw new ArgumentNullException(nameof(context));

    protected ShellFeatureContext Context { get; }

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending document-schema operations and missing diagnostic-record streams are applied automatically at startup instead of requiring Groundwork.Tool. Drift and destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;
}
