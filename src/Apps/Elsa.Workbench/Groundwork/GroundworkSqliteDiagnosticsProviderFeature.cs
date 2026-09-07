using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Opt-in second Groundwork SQLite connection, on its own database file, for the diagnostics stores.
/// A Groundwork SQLite connection (0.4.0-preview.9 through preview.17) serializes every session open behind
/// any open unit of work on the same connection, so a diagnostics drain sharing the runtime's connection
/// doubles HTTP latency (issue #1569; upstream valence-works/groundwork-v2#424). The default shell keeps one
/// SQLite file by owner decision; hosts that need the separation before the upstream fix enable this feature
/// and set <c>DiagnosticsGroundworkPersistence.Target</c> to <see cref="DefaultTarget"/>.
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
