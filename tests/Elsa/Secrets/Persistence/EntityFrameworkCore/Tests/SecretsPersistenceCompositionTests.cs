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
            ConnectionString = "Data Source=:memory:",
            MigratePolicy = EfMigratePolicy.Validate
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
                    "Features": {
                      "SecretsEntityFrameworkCore": {
                        "Provider": "Sqlite",
                        "ConnectionString": "Data Source={{path.Replace("\\", "/")}};Pooling=False",
                        "MigratePolicy": "AutoMigrate"
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

    private static string BackendName(IServiceCollection services) =>
        Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()).Name;

}
