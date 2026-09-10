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
        Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        Assert.IsType<SecretsSqliteDbContext>(first.ServiceProvider.GetRequiredService<SecretsDbContext>());
        Assert.NotSame(repository, second.ServiceProvider.GetRequiredService<ISecretRepository>());
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISecretRepository)).Lifetime);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(SecretsEfMigrationHostedService));
    }

    [Fact]
    public void Registration_refuses_a_prior_groundwork_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRepositoryBackend(SecretRepositoryBackend.Groundwork));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));
        Assert.Contains(SecretRepositoryBackend.Groundwork, exception.Message, StringComparison.Ordinal);
        Assert.Contains(SecretRepositoryBackend.EntityFramework, exception.Message, StringComparison.Ordinal);
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
