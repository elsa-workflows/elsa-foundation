using System.Text.Json;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// EF Core is the Secrets persistence family. Configuration-driven cases
/// bind features from JSON the same way Workbench <c>shells.json</c> does, after the host
/// catalog discovers feature assemblies. Create/resolve/restart journeys live in
/// <see cref="SecretsPersistenceHostJourneyTests"/>.
/// </summary>
public sealed class SecretsPersistenceCompositionTests
{
    private const string ShellName = "secrets-persistence";
    private const string EntityFrameworkBackend = "entity-framework";

    [Fact]
    public void Entity_framework_feature_alone_selects_the_ef_repository()
    {
        var services = new ServiceCollection();
        new SecretsEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);

        Assert.Equal(EntityFrameworkBackend, BackendName(services));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfSecretRepository>(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
    }

    [Fact]
    public async Task Configuration_selects_entity_framework_from_the_assembly_catalog()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-composition-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = await SecretsHostCatalog.StartAsync(
                $$"""
            {
              "CShells": {
                "Shells": {
                  "secrets-persistence": {
                    "Name": "secrets-persistence",
                    "Configuration": {
                      "Elsa": { "Persistence": { "EntityFramework": { "Migrate": { "Policy": "AutoMigrate" } } } }
                    },
                    "Features": {
                      "SecretsEntityFrameworkCore": {
                        "Provider": "Sqlite",
                        "ConnectionString": "Data Source={{path.Replace("\\", "/")}};Pooling=False"
                      }
                    }
                  }
                }
              }
            }
            """);

            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            Assert.IsType<EfSecretRepository>(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
            Assert.Equal(
                EntityFrameworkBackend,
                scope.ServiceProvider.GetRequiredService<SecretRepositoryBackend>().Name);
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    /// <summary>
    /// FR-058's non-additive half, through a real CShells shell rather than a direct
    /// <c>ConfigureServices</c> call: a host that still carries the retired Secrets-only setting must fail to
    /// start, naming the host-wide key to move the value to. Nothing else in the composition is wrong, which
    /// is the point — the shell is refused for the setting alone.
    /// </summary>
    [Fact]
    public async Task A_shell_that_still_sets_the_retired_MigratePolicy_fails_to_start()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-retired-policy-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = await SecretsHostCatalog.StartAsync(Shells(path, retiredPolicy: "Validate"));

            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

            Assert.Contains("Elsa:Persistence:EntityFramework:Migrate:Policy", Messages(failure), StringComparison.Ordinal);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    /// <summary>The other half: the same shell without the retired setting starts and migrates normally.</summary>
    [Fact]
    public async Task A_shell_that_does_not_set_the_retired_MigratePolicy_starts_normally()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-host-wide-policy-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = await SecretsHostCatalog.StartAsync(Shells(path, retiredPolicy: null));

            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            Assert.IsType<EfSecretRepository>(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    /// <summary>The whole exception chain's text: CShells wraps a feature's refusal before rethrowing it.</summary>
    private static string Messages(Exception failure)
    {
        var text = new System.Text.StringBuilder();
        for (var current = failure; current is not null; current = current.InnerException)
            text.AppendLine(current.Message);
        return text.ToString();
    }

    private static string Shells(string path, string? retiredPolicy)
    {
        var connection = $"Data Source={path.Replace("\\", "/")};Pooling=False";
        var secrets = new Dictionary<string, object> { ["Provider"] = "Sqlite", ["ConnectionString"] = connection };
        if (retiredPolicy is not null)
            secrets["MigratePolicy"] = retiredPolicy;

        return JsonSerializer.Serialize(new
        {
            CShells = new
            {
                Shells = new Dictionary<string, object>
                {
                    [ShellName] = new
                    {
                        Name = ShellName,
                        // The host-wide key, on this shell's own Configuration node: the layer that survives
                        // the retirement of the Secrets-only feature setting (ADR 0076 D8).
                        Configuration = new { Elsa = new { Persistence = new { EntityFramework = new { Migrate = new { Policy = "AutoMigrate" } } } } },
                        Features = new Dictionary<string, object> { ["SecretsEntityFrameworkCore"] = secrets }
                    }
                }
            }
        });
    }

    private static void DeleteSqliteFiles(string path)
    {
        File.Delete(path);
        File.Delete($"{path}-wal");
        File.Delete($"{path}-shm");
    }

    private static string BackendName(IServiceCollection services) =>
        Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()).Name;

}
