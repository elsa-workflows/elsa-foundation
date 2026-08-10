using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql;

/// <summary>
/// Contributes the PostgreSQL Groundwork provider leaf. Enabling this makes <c>postgresql</c> a provider
/// that <c>GroundworkTargets</c> entries may name; it opens no store on its own.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderPostgreSql",
    DisplayName = "Groundwork PostgreSQL Provider",
    Description = "Makes 'postgresql' available as a Groundwork target provider. Declare the databases themselves on GroundworkTargets; this feature only supplies the leaf that opens them.")]
public class PostgreSqlGroundworkProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkProviderLeaf(new PostgreSqlGroundworkProviderLeaf());
}

/// <summary>Opens PostgreSQL-backed Groundwork targets.</summary>
public sealed class PostgreSqlGroundworkProviderLeaf : IGroundworkProviderLeaf
{
    public string ProviderIdentity => PostgreSqlGroundworkDocumentStoreRegistration.ProviderIdentity;

    public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        entry.EnsureOnlyKnownOptions(targetName);
        services.AddPostgreSqlGroundworkDocumentStore(
            entry.RequireConnectionString(targetName),
            entry.AutoApplySchemaOnStartup,
            targetName);
    }
}
