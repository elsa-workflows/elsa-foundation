using System.Reflection;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Options;
using Groundwork.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Workbench-shaped host catalog for Phase 3 journeys. Testhost is the process entry assembly, so
/// <c>WithHostAssemblies()</c> cannot see Workbench; this loads the test project's referenced
/// feature assemblies the same way Workbench's ProjectReference catalog does.
/// </summary>
internal static class SecretsHostCatalog
{
    public const string ShellName = "secrets-persistence";
    public const string EncryptionKey = "phase-3-journey-key";
    public const string TenantId = "tenant-a";

    public static Assembly[] Assemblies()
    {
        var host = typeof(SecretsHostCatalog).Assembly;
        var loaded = new HashSet<Assembly> { host };
        foreach (var name in host.GetReferencedAssemblies())
        {
            try
            {
                loaded.Add(Assembly.Load(name));
            }
            catch (Exception)
            {
                // Optional host references stay optional, matching WithHostAssemblies skip-on-miss.
            }
        }

        return loaded.ToArray();
    }

    public static async Task<WebApplication> StartAsync(string shellsJson)
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-host-catalog-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, shellsJson);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
            builder.Services.AddCShellsAspNetCore(shells =>
            {
                shells
                    .WithHostAssemblies()
                    .WithAssemblies(Assemblies())
                    .WithConfigurationProvider(builder.Configuration);
            });

            var app = builder.Build();
            app.MapShells();
            await app.StartAsync();
            return app;
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Same feature name and connection settings as Workbench's <c>GroundworkProviderSqlite</c>.
/// The Workbench type lives in the host app; architecture tests keep that app's test surface
/// on <c>Elsa.Modularity.Tests</c>, so this catalog equivalent uses the public provider factory.
/// </summary>
[ShellFeature(
    name: "GroundworkProviderSqlite",
    DisplayName = "Groundwork SQLite Provider",
    Description = "Test-host catalog equivalent of Workbench GroundworkProviderSqlite.")]
public sealed class HostCatalogGroundworkSqliteProviderFeature : IShellFeature
{
    public string? ConnectionString { get; set; }

    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkStorageProviderConnection(
            _ => new SqliteProviderFactory().Create(
                string.IsNullOrWhiteSpace(ConnectionString)
                    ? "Data Source=elsa-groundwork.db"
                    : ConnectionString),
            Target);
}

/// <summary>
/// <c>SecretsFeature</c> calls <c>AddSecrets()</c> without the host <c>IConfiguration</c>, so
/// journeys bind the encryption key from shells.json the same way a host would configure options.
/// </summary>
[ShellFeature(name: "SecretsJourneyEncryption")]
public sealed class SecretsJourneyEncryptionFeature : IShellFeature
{
    public string EncryptionKey { get; set; } = SecretsHostCatalog.EncryptionKey;

    public void ConfigureServices(IServiceCollection services)
    {
        var key = EncryptionKey;
        services.PostConfigure<SecretsOptions>(options =>
            options.EncryptionKey = string.IsNullOrWhiteSpace(options.EncryptionKey) ? key : options.EncryptionKey);
    }
}
