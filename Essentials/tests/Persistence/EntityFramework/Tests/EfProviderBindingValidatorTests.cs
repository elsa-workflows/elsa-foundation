using CShells.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// This project references the Sqlite engine only, so any other provider name is one whose package is not loadable
/// here — the shape of a host that configured a module for a provider it never referenced.
/// </summary>
public sealed class EfProviderBindingValidatorTests
{
    private readonly ServiceCollection services = new();

    private sealed class SqliteModuleDbContext(DbContextOptions<SqliteModuleDbContext> options) : DbContext(options);

    private sealed class SqlServerModuleDbContext(DbContextOptions<SqlServerModuleDbContext> options) : DbContext(options);

    private EfProviderBindingValidator Validator()
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<EfProviderBindingValidator>();
    }

    [Fact]
    public void A_module_configured_for_a_loadable_engine_passes()
    {
        services.AddEfModuleMigrations<SqliteModuleDbContext>("Sqlite");

        Validator().Validate();
    }

    [Fact]
    public void A_module_configured_for_an_unloadable_engine_fails_at_startup_naming_what_to_add()
    {
        services.AddEfModuleMigrations<SqlServerModuleDbContext>("SqlServer");

        var exception = Assert.Throws<InvalidOperationException>(() => Validator().Validate());

        Assert.Contains("SqlServerModuleDbContext", exception.Message, StringComparison.Ordinal);
        Assert.Contains("provider 'SqlServer'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("assembly 'Microsoft.EntityFrameworkCore.SqlServer' is not loaded", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions.UseSqlServer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" />", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no migrations ran", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_names_every_failing_module_and_omits_the_ones_that_bind()
    {
        services.AddEfModuleMigrations<SqliteModuleDbContext>("Sqlite");
        services.AddEfModuleMigrations<SqlServerModuleDbContext>("PostgreSql");
        services.AddEfProviderBindingValidation<DbContext>("MySql");

        var exception = Assert.Throws<InvalidOperationException>(() => Validator().Validate());

        Assert.Contains("2 configured EF modules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MySql.EntityFrameworkCore", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteModuleDbContext", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_registering_a_context_validates_the_provider_its_migrator_will_apply()
    {
        // Several features share one context; AddEfModuleMigrations keeps the last provider configured for it, and
        // validating an earlier one would demand an engine no module actually binds.
        services.AddEfModuleMigrations<SqliteModuleDbContext>("SqlServer");
        services.AddEfModuleMigrations<SqliteModuleDbContext>("Sqlite");

        Validator().Validate();
    }

    [Fact]
    public void The_validator_is_prepared_before_every_module_migrator()
    {
        services.AddEfModuleMigrations<SqliteModuleDbContext>("Sqlite");

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<ShellInitializerRegistration>().ToArray();
        var validator = registrations.Single(registration => registration.InitializerType == typeof(EfProviderBindingValidator));
        var migrator = registrations.Single(registration => registration.InitializerType == typeof(EfModuleMigrator<SqliteModuleDbContext>));

        Assert.Equal(LifecyclePhase.Prepare, validator.Phase);
        Assert.Equal(LifecyclePhase.Prepare, migrator.Phase);
        Assert.True(validator.Order < migrator.Order, $"validator order {validator.Order} must precede migrator order {migrator.Order}");
    }

    [Fact]
    public void The_validator_starts_before_every_module_migrator_on_a_plain_host()
    {
        services.AddEfModuleMigrations<SqliteModuleDbContext>("Sqlite");
        services.AddEfModuleMigrations<SqlServerModuleDbContext>("Sqlite");

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();

        Assert.Equal(0, Array.FindIndex(hosted, service => service is EfProviderBindingValidator));
        Assert.All(
            hosted.Skip(1),
            service => Assert.True(service is EfModuleMigrator<SqliteModuleDbContext> or EfModuleMigrator<SqlServerModuleDbContext>));
    }
}
