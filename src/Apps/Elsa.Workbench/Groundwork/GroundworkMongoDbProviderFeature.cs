using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Selects the public Groundwork MongoDB provider connection for an Elsa shell.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderMongoDb",
    DisplayName = "Groundwork MongoDB Provider",
    Description = "Provides a public Groundwork MongoDB connection for Elsa persistence features.")]
public class GroundworkMongoDbProviderFeature : GroundworkProviderFeatureBase
{
    public const string DefaultConnectionString = "mongodb://localhost:27017/elsa?replicaSet=rs0";

    protected override string DefaultConnectionStringValue => DefaultConnectionString;

    protected override void ConfigureProvider(IServiceCollection services, string connectionString) =>
        services.AddGroundworkMongoDbProvider(connectionString, Target);
}
