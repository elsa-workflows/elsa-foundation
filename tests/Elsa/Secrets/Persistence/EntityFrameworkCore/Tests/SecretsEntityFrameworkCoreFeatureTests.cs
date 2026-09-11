using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsEntityFrameworkCoreFeatureTests
{
    [Fact]
    public void Connection_string_manifest_setting_is_secret()
    {
        var property = typeof(SecretsEntityFrameworkCoreFeature).GetProperty(nameof(SecretsEntityFrameworkCoreFeature.ConnectionString));
        var setting = Assert.Single(
            property!.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName ==
                         "Elsa.Platform.PackageManifest.Generator.Hints.ManifestSettingAttribute");

        Assert.Contains(
            setting.NamedArguments,
            argument => argument.MemberName == "Secret" && argument.TypedValue.Value is true);
    }

    [Fact]
    public void Feature_registers_the_repository_and_sqlite_context()
    {
        var feature = new SecretsEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            MigratePolicy = EfMigratePolicy.AutoMigrate
        };
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var repository = first.ServiceProvider.GetRequiredService<ISecretRepository>();
        Assert.IsType<EfSecretRepository>(repository);
        Assert.Equal("entity-framework", Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()).Name);
        Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        Assert.IsType<SecretsSqliteDbContext>(first.ServiceProvider.GetRequiredService<SecretsDbContext>());
        Assert.NotSame(repository, second.ServiceProvider.GetRequiredService<ISecretRepository>());
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISecretRepository)).Lifetime);
        var hosted = Assert.Single(provider.GetServices<IHostedService>().OfType<SecretsEfMigrationHostedService>());
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>().OfType<SecretsEfMigrationHostedService>());
        Assert.Same(hosted, initializer);
    }

    [Theory]
    [InlineData("SqlServer", typeof(SecretsSqlServerDbContext))]
    [InlineData("PostgreSql", typeof(SecretsPostgreSqlDbContext))]
    public void Feature_binds_the_derived_context_for_the_selected_provider(string provider, Type contextType)
    {
        var feature = new SecretsEntityFrameworkCoreFeature
        {
            Provider = provider,
            ConnectionString = "Host=unused;Database=unused"
        };
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        Assert.Contains(services, descriptor => descriptor.ServiceType == contextType);
        Assert.Equal("entity-framework", services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()
            .Single().Name);
    }

    [Fact]
    public void Registration_refuses_a_prior_groundwork_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRepositoryBackend("groundwork"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));
        Assert.Contains("groundwork", exception.Message, StringComparison.Ordinal);
        Assert.Contains("entity-framework", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_replaces_a_prior_repository_registration()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISecretRepository, StubSecretRepository>()
            .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            });

        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(StubSecretRepository));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISecretRepository));
    }

    private sealed class StubSecretRepository : ISecretRepository
    {
        public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Secret?>(null);

        public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SecretRepositoryPage([], 0));
    }
}
