using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Shared settings of the per-provider Groundwork runtime persistence features
/// (SQLite, PostgreSQL, SQL Server, MongoDB). Connection selection stays per provider.
/// </summary>
public abstract class GroundworkRuntimePersistenceShellFeatureBase : GroundworkPersistenceShellFeatureBase
{
    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending schema operations are applied automatically at startup instead of requiring Groundwork.Tool. Destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;
}
