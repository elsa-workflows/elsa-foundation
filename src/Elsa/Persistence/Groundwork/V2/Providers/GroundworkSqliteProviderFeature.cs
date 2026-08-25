using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Providers;

/// <summary>
/// Shared operator settings for a public Groundwork provider connection.
/// </summary>
public abstract class GroundworkProviderFeatureBase : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Provider connection string used by Groundwork persistence features.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Target",
        Description = "Optional Groundwork target supplied by this provider. Defaults to 'default'.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        ConfigureProvider(
            services,
            string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionStringValue : ConnectionString);

    protected abstract string DefaultConnectionStringValue { get; }

    protected abstract void ConfigureProvider(IServiceCollection services, string connectionString);
}

/// <summary>
/// Selects the public Groundwork SQLite provider connection for an Elsa shell.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkProviderSqlite",
    DisplayName = "Groundwork SQLite Provider",
    Description = "Provides a public Groundwork SQLite connection for Elsa persistence features.")]
public class GroundworkSqliteProviderFeature : GroundworkProviderFeatureBase
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork.db";

    protected override string DefaultConnectionStringValue => DefaultConnectionString;

    protected override void ConfigureProvider(IServiceCollection services, string connectionString) =>
        services.AddGroundworkSqliteProvider(connectionString, Target);
}
