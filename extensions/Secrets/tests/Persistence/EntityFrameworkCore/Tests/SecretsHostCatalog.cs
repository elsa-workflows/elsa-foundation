using System.IO;
using System.Reflection;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Secrets.Features;
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

        // GetReferencedAssemblies lists only assemblies this project's code names a type from. Nothing here names one
        // from Elsa.Secrets itself, since the contracts live in Elsa.Secrets.Core, so the feature assembly is seeded.
        var loaded = new HashSet<Assembly> { host, typeof(SecretsFeature).Assembly };
        foreach (var name in host.GetReferencedAssemblies())
        {
            try
            {
                loaded.Add(Assembly.Load(name));
            }
            catch (FileNotFoundException)
            {
                // Optional host references stay optional, matching WithHostAssemblies skip-on-miss.
            }
            catch (FileLoadException)
            {
                // Optional host references stay optional, matching WithHostAssemblies skip-on-miss.
            }
            catch (BadImageFormatException)
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
