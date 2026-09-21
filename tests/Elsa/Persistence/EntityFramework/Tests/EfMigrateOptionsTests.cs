using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The migrate policy is an operator setting. It is bound from the configuration of whichever container the
/// migrator resolves its options from — a CShells shell or a plain host — it stays AutoMigrate when nothing is
/// configured, and a value that names no policy refuses to start instead of silently auto-migrating.
/// </summary>
public sealed class EfMigrateOptionsTests : IDisposable
{
    private const string PolicyKey = $"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}";
    private const string ShellName = "ef-migrate-policy";

    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-ef-migrate-policy-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public void An_absent_key_keeps_the_AutoMigrate_default() =>
        Assert.Equal(EfMigratePolicy.AutoMigrate, Policy(Configured()));

    /// <summary>A container with no configuration at all is the case every unit test and test host is in.</summary>
    [Fact]
    public void A_container_without_configuration_keeps_the_AutoMigrate_default() =>
        Assert.Equal(EfMigratePolicy.AutoMigrate, Policy(new ServiceCollection()));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_value_keeps_the_AutoMigrate_default(string configured) =>
        Assert.Equal(EfMigratePolicy.AutoMigrate, Policy(Configured(configured)));

    [Theory]
    [InlineData("Validate", EfMigratePolicy.Validate)]
    [InlineData("validate", EfMigratePolicy.Validate)]
    [InlineData("VALIDATE", EfMigratePolicy.Validate)]
    [InlineData("AutoMigrate", EfMigratePolicy.AutoMigrate)]
    public void A_configured_policy_is_honored(string configured, EfMigratePolicy expected) =>
        Assert.Equal(expected, Policy(Configured(configured)));

    [Theory]
    [InlineData("Nope")]
    [InlineData("true")]
    [InlineData("17")]
    [InlineData("Valid ate")]
    public void A_value_that_names_no_policy_is_refused_instead_of_defaulting(string configured)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Policy(Configured(configured)));

        Assert.Equal(
            $"Configuration '{PolicyKey}' is '{configured}'. Use 'AutoMigrate' or 'Validate'.",
            exception.Message);
    }

    /// <summary>
    /// Configuration is the operator's lever; a host that still sets the policy in code after composing the module
    /// keeps the last word, which is what <c>IConfigureOptions</c> ordering means and what existing hosts rely on.
    /// </summary>
    [Fact]
    public void Code_configured_after_the_module_wins_over_the_configured_value()
    {
        var services = Migrations(Configured("Validate"));
        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);

        Assert.Equal(EfMigratePolicy.AutoMigrate, Resolve(services));
    }

    /// <summary>However many modules register a migrator, the policy is bound once.</summary>
    [Fact]
    public void Repeated_module_registrations_bind_the_policy_once()
    {
        var services = Migrations(Migrations(Configured("Validate")));

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<EfMigrateOptions>));
        Assert.Equal(EfMigratePolicy.Validate, Resolve(services));
    }

    [Fact]
    public async Task A_configured_Validate_stops_a_plain_host_on_a_database_with_pending_migrations()
    {
        await using var provider = Migrations(Configured("Validate")).BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            provider.GetRequiredService<IShellInitializer>().InitializeAsync());

        Assert.Contains(EfTestMigrationIds.Initial, exception.Message, StringComparison.Ordinal);
        Assert.Contains(PolicyKey, exception.Message, StringComparison.Ordinal);
        Assert.Empty(await AppliedMigrationsAsync());
    }

    [Fact]
    public async Task An_unconfigured_plain_host_applies_the_migrations()
    {
        await using var provider = Migrations(Configured()).BuildServiceProvider();

        await provider.GetRequiredService<IShellInitializer>().InitializeAsync();

        Assert.Equal(
            [EfTestMigrationIds.Initial, EfTestMigrationIds.AddDescription],
            await AppliedMigrationsAsync());
    }

    /// <summary>
    /// The shell container's <c>IConfiguration</c> is CShells' own, layering the shell's <c>Configuration</c> node
    /// over the host's. Both places an operator can write the key must reach the module's migrator: a shell that
    /// could not see the key would auto-migrate and look exactly like a healthy start.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData($"CShells:Shells:{ShellName}:Configuration:")]
    public async Task A_configured_Validate_stops_shell_activation_on_a_database_with_pending_migrations(string keyPrefix)
    {
        await using var host = ShellHost((keyPrefix + PolicyKey, "Validate"));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var messages = Flatten(exception);
        Assert.Contains(EfTestMigrationIds.Initial, messages, StringComparison.Ordinal);
        Assert.Contains(PolicyKey, messages, StringComparison.Ordinal);
        Assert.Empty(await AppliedMigrationsAsync());
    }

    [Fact]
    public async Task An_unconfigured_shell_applies_the_migrations_on_activation()
    {
        await using var host = ShellHost();

        await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        Assert.Equal(
            [EfTestMigrationIds.Initial, EfTestMigrationIds.AddDescription],
            await AppliedMigrationsAsync());
    }

    [Fact]
    public async Task A_shell_refuses_a_value_that_names_no_policy()
    {
        await using var host = ShellHost((PolicyKey, "Nope"));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        Assert.Contains($"Configuration '{PolicyKey}' is 'Nope'.", Flatten(exception), StringComparison.Ordinal);
        Assert.Empty(await AppliedMigrationsAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }

    /// <summary>A real CShells host composed from configuration, as a <c>shells.json</c> composes one.</summary>
    private ServiceProvider ShellHost(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings
                .Select(setting => KeyValuePair.Create(setting.Key, setting.Value))
                .Append(KeyValuePair.Create(
                    $"CShells:Shells:{ShellName}:Features:{MigratingFeature.FeatureName}:{nameof(MigratingFeature.ConnectionString)}",
                    (string?)ConnectionString)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(EfMigrateOptionsTests).Assembly)
            .WithConfigurationProvider(configuration));
        return services.BuildServiceProvider();
    }

    private async Task<string[]> AppliedMigrationsAsync()
    {
        await using var context = NewContext(ConnectionString);
        return [.. await context.Database.GetAppliedMigrationsAsync()];
    }

    private IServiceCollection Migrations(IServiceCollection services) => AddMigratingModule(services, ConnectionString);

    private EfMigratePolicy Policy(IServiceCollection services) => Resolve(Migrations(services));

    private static IServiceCollection Configured(string? policy = null) =>
        new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(policy is null ? [] : [new KeyValuePair<string, string?>(PolicyKey, policy)])
            .Build());

    /// <summary>What every first-party EF module registers: its context, and the migrator the policy governs.</summary>
    private static IServiceCollection AddMigratingModule(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<EfDatabaseMigratorTests.MigratorContext>(builder => Sqlite(builder, connectionString));
        return services.AddEfModuleMigrations<EfDatabaseMigratorTests.MigratorContext>("Sqlite");
    }

    private static EfMigratePolicy Resolve(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<EfMigrateOptions>>().Value.Policy;
    }

    private static EfDatabaseMigratorTests.MigratorContext NewContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<EfDatabaseMigratorTests.MigratorContext>();
        Sqlite(builder, connectionString);
        return new(builder.Options);
    }

    private static void Sqlite(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(EfMigrateOptionsTests).Assembly.GetName().Name));

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
    }

    /// <summary>
    /// Stands in for a module's EF shell feature: it registers the module context and its migrator, which is all
    /// the policy governs. The context and its two migrations are <see cref="EfDatabaseMigratorTests"/>'.
    /// </summary>
    [ShellFeature(FeatureName)]
    public sealed class MigratingFeature : IShellFeature
    {
        public const string FeatureName = "EfMigratePolicyTestModule";

        public string ConnectionString { get; set; } = "";

        public void ConfigureServices(IServiceCollection services) => AddMigratingModule(services, ConnectionString);
    }
}
