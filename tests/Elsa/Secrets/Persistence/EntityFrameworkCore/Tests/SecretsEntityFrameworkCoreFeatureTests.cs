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
            ConnectionString = "Data Source=:memory:"
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
        var hosted = Assert.Single(provider.GetServices<IHostedService>().OfType<EfModuleMigrator<SecretsDbContext>>());
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>().OfType<EfModuleMigrator<SecretsDbContext>>());
        Assert.Same(hosted, initializer);
    }

    /// <summary>
    /// The retired <c>MigratePolicy</c> setting (ADR 0076 D8, FR-058). The property stays for one release so
    /// CShells' binder still reads a host's configured value at all — a deleted property is never even looked
    /// at, and a throwing setter is swallowed — and the refusal happens where a shell cannot miss it, in the
    /// feature's own service-configuration path.
    /// </summary>
    [Fact]
    public void A_non_null_retired_MigratePolicy_refuses_to_configure_and_names_the_host_wide_key()
    {
#pragma warning disable CS0618 // Setting it is exactly what must fail.
        var feature = new SecretsEntityFrameworkCoreFeature { Provider = "Sqlite", MigratePolicy = "Validate" };
#pragma warning restore CS0618

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains("Elsa:Persistence:EntityFramework:Migrate:Policy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SecretsEntityFrameworkCore:MigratePolicy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Validate", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The retired property has to bind without throwing, and has to bind <i>whatever</i> the host configured:
    /// its type is <see cref="string"/> rather than the enum precisely so a value the enum cannot parse still
    /// reaches the feature instead of vanishing inside the binder's swallowed set.
    /// </summary>
    [Theory]
    [InlineData("AutoMigrate")]
    [InlineData("Validate")]
    [InlineData("nonsense")]
    public void Any_non_null_retired_MigratePolicy_value_refuses(string configured)
    {
#pragma warning disable CS0618
        var feature = new SecretsEntityFrameworkCoreFeature { Provider = "Sqlite", MigratePolicy = configured };
#pragma warning restore CS0618

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    /// <summary>
    /// CShells binds a configuration key only onto a property of that exact name that is public and settable
    /// (<c>FeatureConfigurationBinder.AutoBindFeatureProperties</c>), so deleting the retired property would
    /// make a still-configured value invisible rather than loud. This pins the two facts the refusal depends
    /// on, because neither is visible from the refusal itself.
    /// </summary>
    [Fact]
    public void The_retired_property_is_still_publicly_settable_and_nullable_so_the_binder_reaches_it()
    {
        var property = typeof(SecretsEntityFrameworkCoreFeature).GetProperty("MigratePolicy");

        Assert.NotNull(property);
        Assert.True(property!.CanWrite);
        Assert.True(property.GetSetMethod()?.IsPublic);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Null(property.GetValue(new SecretsEntityFrameworkCoreFeature()));
        Assert.Contains(
            property.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(ObsoleteAttribute));
    }

    [Theory]
    [InlineData("MySql", typeof(SecretsMySqlDbContext))]
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
    public void Registration_refuses_a_prior_foreign_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRepositoryBackend("custom"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));
        Assert.Contains("custom", exception.Message, StringComparison.Ordinal);
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
