using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Provides a second Groundwork SQLite connection, on its own database file, for the diagnostics stores.
/// A Groundwork SQLite connection serializes every session open and unit of work on one gate, so the
/// diagnostics drain must not share the runtime's connection: with one shared file every HTTP request
/// waited behind the drain's batch commits and the HTTP workflow p95 doubled (issue #1569). Pair this
/// feature with <c>DiagnosticsGroundworkPersistence</c> targeting <see cref="DefaultTarget"/>.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderSqliteDiagnostics",
    DisplayName = "Groundwork SQLite Diagnostics Provider",
    Description = "Provides a Groundwork SQLite connection on a separate database file for the diagnostics stores.")]
public class GroundworkSqliteDiagnosticsProviderFeature : GroundworkProviderFeatureBase
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork-diagnostics.db";
    public const string DefaultTarget = "diagnostics";

    protected override string DefaultConnectionStringValue => DefaultConnectionString;

    protected override void ConfigureProvider(IServiceCollection services, string connectionString) =>
        services.AddGroundworkSqliteProvider(
            connectionString,
            string.IsNullOrWhiteSpace(Target) ? DefaultTarget : Target);
}
