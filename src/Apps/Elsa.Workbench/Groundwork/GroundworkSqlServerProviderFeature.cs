using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Selects the public Groundwork SQL Server provider connection for an Elsa shell.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderSqlServer",
    DisplayName = "Groundwork SQL Server Provider",
    Description = "Provides a public Groundwork SQL Server connection for Elsa persistence features.")]
public class GroundworkSqlServerProviderFeature : GroundworkProviderFeatureBase
{
    public const string DefaultConnectionString =
        "Server=localhost,1433;Database=elsa;Integrated Security=True;TrustServerCertificate=True";

    protected override string DefaultConnectionStringValue => DefaultConnectionString;

    protected override void ConfigureProvider(IServiceCollection services, string connectionString) =>
        services.AddGroundworkSqlServerProvider(connectionString, Target);
}
