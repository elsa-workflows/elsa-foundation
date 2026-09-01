using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Shared operator settings for a Workbench-selected Groundwork provider connection.
/// </summary>
public abstract class GroundworkProviderFeatureBase : IShellFeature
{
    public string? ConnectionString { get; set; }

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
