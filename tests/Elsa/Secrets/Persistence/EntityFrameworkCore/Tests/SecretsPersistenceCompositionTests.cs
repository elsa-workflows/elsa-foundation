using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.Groundwork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Phase 3: a shell selects EF or Groundwork, never both. Default hosts stay on Groundwork
/// (see <c>SecretsEfPersistencePilotArchitectureTests</c>). Configuration-driven cases
/// bind features from JSON the same way Workbench <c>shells.json</c> does, after the host
/// catalog discovers feature assemblies. Create/resolve/restart journeys live in
/// <see cref="SecretsPersistenceHostJourneyTests"/>.
/// </summary>
public sealed class SecretsPersistenceCompositionTests
{
    private const string ShellName = "secrets-persistence";

    [Fact]
    public void Groundwork_feature_alone_selects_the_groundwork_repository()
    {
        var services = new ServiceCollection();
        new SecretsGroundworkPersistenceFeature().ConfigureServices(services);

        Assert.Equal(SecretRepositoryBackend.Groundwork, BackendName(services));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISecretRepository) &&
            descriptor.ImplementationFactory is not null);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(EfSecretRepository));
    }

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

        Assert.Equal(SecretRepositoryBackend.EntityFramework, BackendName(services));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfSecretRepository>(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Configuring_both_features_fails_in_either_order(bool entityFrameworkFirst)
    {
        var services = new ServiceCollection();
        Action first = entityFrameworkFirst ? ConfigureEntityFramework : ConfigureGroundwork;
        Action second = entityFrameworkFirst ? ConfigureGroundwork : ConfigureEntityFramework;

        first();
        var exception = Assert.Throws<InvalidOperationException>(second);
        Assert.Contains(SecretRepositoryBackend.Groundwork, exception.Message, StringComparison.Ordinal);
        Assert.Contains(SecretRepositoryBackend.EntityFramework, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Enable only one Secrets persistence feature", exception.Message, StringComparison.Ordinal);
        return;

        void ConfigureEntityFramework() =>
            new SecretsEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }.ConfigureServices(services);

        void ConfigureGroundwork() =>
            new SecretsGroundworkPersistenceFeature().ConfigureServices(services);
    }

    [Fact]
    public async Task CShells_activation_refuses_both_features_in_one_shell()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shells
                .WithHostAssemblies()
                .WithAssemblies(SecretsHostCatalog.Assemblies())
                .AddShell(ShellName, shell =>
                {
                    shell.WithFeature<SecretsEntityFrameworkCoreFeature>(feature =>
                    {
                        feature.Provider = "Sqlite";
                        feature.ConnectionString = "Data Source=:memory:";
                    });
                    shell.WithFeature<SecretsGroundworkPersistenceFeature>();
                });
        });

        await using var app = builder.Build();
        app.MapShells();
        await app.StartAsync();

        var registry = app.Services.GetRequiredService<IShellRegistry>();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => registry.GetOrActivateAsync(ShellName));
        var flattened = Flatten(exception);
        Assert.Contains(SecretRepositoryBackend.Groundwork, flattened, StringComparison.Ordinal);
        Assert.Contains(SecretRepositoryBackend.EntityFramework, flattened, StringComparison.Ordinal);
        Assert.Contains("Enable only one Secrets persistence feature", flattened, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_selects_entity_framework_from_the_assembly_catalog()
    {
        await using var app = await SecretsHostCatalog.StartAsync(
            """
            {
              "CShells": {
                "Shells": {
                  "secrets-persistence": {
                    "Name": "secrets-persistence",
                    "Features": {
                      "SecretsEntityFrameworkCore": {
                        "Provider": "Sqlite",
                        "ConnectionString": "Data Source=:memory:",
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
            SecretRepositoryBackend.EntityFramework,
            scope.ServiceProvider.GetRequiredService<SecretRepositoryBackend>().Name);
    }

    [Fact]
    public async Task Configuration_refuses_both_features_from_the_assembly_catalog()
    {
        await using var app = await SecretsHostCatalog.StartAsync(
            """
            {
              "CShells": {
                "Shells": {
                  "secrets-persistence": {
                    "Name": "secrets-persistence",
                    "Features": {
                      "SecretsEntityFrameworkCore": {
                        "Provider": "Sqlite",
                        "ConnectionString": "Data Source=:memory:"
                      },
                      "SecretsGroundworkPersistence": {}
                    }
                  }
                }
              }
            }
            """);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));
        var flattened = Flatten(exception);
        Assert.Contains(SecretRepositoryBackend.Groundwork, flattened, StringComparison.Ordinal);
        Assert.Contains(SecretRepositoryBackend.EntityFramework, flattened, StringComparison.Ordinal);
        Assert.Contains("Enable only one Secrets persistence feature", flattened, StringComparison.Ordinal);
    }

    private static string BackendName(IServiceCollection services) =>
        Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()).Name;

    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.Message);
        return string.Join(" | ", parts);
    }
}
