using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Contributes the SQLite Groundwork provider leaf. Enabling this makes <c>sqlite</c> a provider that
/// <c>GroundworkTargets</c> entries may name; it opens no store on its own.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderSqlite",
    DisplayName = "Groundwork SQLite Provider",
    Description = "Makes 'sqlite' available as a Groundwork target provider. Declare the databases themselves on GroundworkTargets; this feature only supplies the leaf that opens them.")]
public class SqliteGroundworkProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkProviderLeaf(new SqliteGroundworkProviderLeaf());
}

/// <summary>Opens SQLite-backed Groundwork targets, binding their opaque options to SQLite's own settings.</summary>
public sealed class SqliteGroundworkProviderLeaf : IGroundworkProviderLeaf
{
    /// <summary>Retain a bounded shell-local cache of immutable executable artifacts loaded from this store.</summary>
    public const string StoreCacheCapacityOption = "StoreCacheCapacity";

    /// <summary>
    /// Record an applied-plan fingerprint and skip the full inspection walk on later boots whose composed
    /// plan is unchanged. Off by default: the fingerprint proves the plan is current but cannot detect
    /// schema changed out of band while the host was down.
    /// </summary>
    public const string SkipSchemaInspectionWhenPlanUnchangedOption = "SkipSchemaInspectionWhenPlanUnchanged";

    public string ProviderIdentity => SqliteGroundworkDocumentStoreRegistration.ProviderIdentity;

    public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        entry.EnsureOnlyKnownOptions(
            targetName,
            StoreCacheCapacityOption,
            SkipSchemaInspectionWhenPlanUnchangedOption);

        var storeCacheCapacity = entry.GetPositiveInt32(targetName, StoreCacheCapacityOption);
        services.AddSqliteGroundworkDocumentStore(
            entry.RequireConnectionString(targetName),
            entry.AutoApplySchemaOnStartup,
            entry.GetBoolean(targetName, SkipSchemaInspectionWhenPlanUnchangedOption, false),
            storeCacheCapacity is null ? null : new SqliteGroundworkStoreCacheOptions { Capacity = storeCacheCapacity.Value },
            targetName);
    }
}
