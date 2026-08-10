using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.MongoDb;

/// <summary>
/// Contributes the MongoDB Groundwork provider leaf. Enabling this makes <c>mongodb</c> a provider that
/// <c>GroundworkTargets</c> entries may name; it opens no store on its own.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderMongoDb",
    DisplayName = "Groundwork MongoDB Provider",
    Description = "Makes 'mongodb' available as a Groundwork target provider. Declare the databases themselves on GroundworkTargets; this feature only supplies the leaf that opens them. Multi-document design writes need a transaction-capable deployment (replica set or sharded cluster).")]
public class MongoDbGroundworkProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkProviderLeaf(new MongoDbGroundworkProviderLeaf());
}

/// <summary>Opens MongoDB-backed Groundwork targets.</summary>
public sealed class MongoDbGroundworkProviderLeaf : IGroundworkProviderLeaf
{
    /// <summary>The database within the addressed cluster. Two targets on one cluster differ by this.</summary>
    public const string DatabaseNameOption = "DatabaseName";

    public string ProviderIdentity => MongoDbGroundworkDocumentStoreRegistration.ProviderIdentity;

    public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        entry.EnsureOnlyKnownOptions(targetName, DatabaseNameOption);
        services.AddMongoDbGroundworkDocumentStore(
            entry.RequireConnectionString(targetName),
            entry.RequireString(targetName, DatabaseNameOption),
            entry.AutoApplySchemaOnStartup,
            targetName);
    }
}
