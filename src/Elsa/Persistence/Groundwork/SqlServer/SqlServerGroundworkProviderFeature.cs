using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer;

/// <summary>
/// Contributes the SQL Server Groundwork provider leaf. Enabling this makes <c>sqlserver</c> a provider
/// that <c>GroundworkTargets</c> entries may name; it opens no store on its own.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderSqlServer",
    DisplayName = "Groundwork SQL Server Provider",
    Description = "Makes 'sqlserver' available as a Groundwork target provider. Declare the databases themselves on GroundworkTargets; this feature only supplies the leaf that opens them.")]
public class SqlServerGroundworkProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkProviderLeaf(new SqlServerGroundworkProviderLeaf());
}

/// <summary>Opens SQL Server-backed Groundwork targets.</summary>
public sealed class SqlServerGroundworkProviderLeaf : IGroundworkProviderLeaf
{
    public string ProviderIdentity => SqlServerGroundworkDocumentStoreRegistration.ProviderIdentity;

    public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        entry.EnsureOnlyKnownOptions(targetName);
        services.AddSqlServerGroundworkDocumentStore(
            entry.RequireConnectionString(targetName),
            entry.AutoApplySchemaOnStartup,
            targetName);
    }
}
