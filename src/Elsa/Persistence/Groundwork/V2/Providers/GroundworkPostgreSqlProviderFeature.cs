using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Providers;

/// <summary>
/// Selects the public Groundwork PostgreSQL provider connection for an Elsa shell.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderPostgreSql",
    DisplayName = "Groundwork PostgreSQL Provider",
    Description = "Provides a public Groundwork PostgreSQL connection for Elsa persistence features.")]
public class GroundworkPostgreSqlProviderFeature : GroundworkProviderFeatureBase
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    protected override string DefaultConnectionStringValue => DefaultConnectionString;

    protected override void ConfigureProvider(IServiceCollection services, string connectionString) =>
        services.AddGroundworkPostgreSqlProvider(connectionString, Target);
}
