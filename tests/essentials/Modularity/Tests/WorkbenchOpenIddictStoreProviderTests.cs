using Elsa.Persistence.EntityFramework;
using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Elsa.Workbench.OpenIddictEngines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// The engine under Workbench's OpenIddict store is the one the default shell's OpenIddict settings name, SQLite unless they name
/// another, so a deployment of several nodes can put the token store where they all read it: access-token validation reads the
/// token's row, so a token issued on one node is only valid on another when both read one store. Nothing here opens a connection.
/// </summary>
public sealed class WorkbenchOpenIddictStoreProviderTests
{
    private const string Section = "CShells:Shells:default:Features:FoundationIdentityOpenIddict";

    private static readonly (string Provider, Type Context, string ProviderName, string ConnectionString)[] Engines =
    [
        ("Sqlite", typeof(OpenIddictIdentityDbContext), EfProviderNames.Sqlite, "Data Source=tokens.db"),
        ("SqlServer", typeof(OpenIddictIdentitySqlServerDbContext), EfProviderNames.SqlServer, "Server=db;Database=elsa_tokens;User Id=sa;Password=x;TrustServerCertificate=true"),
        ("PostgreSql", typeof(OpenIddictIdentityPostgreSqlDbContext), EfProviderNames.PostgreSql, "Host=db;Database=elsa_tokens;Username=elsa;Password=x")
    ];

    public static TheoryData<string> EngineNames => new(Engines.Select(engine => engine.Provider));

    [Theory]
    [MemberData(nameof(EngineNames))]
    public void The_provider_setting_selects_the_engines_context_on_the_stores_connection_string(string provider)
    {
        var (_, context, providerName, connectionString) = Engines.Single(engine => engine.Provider == provider);
        using var services = CreateProvider(provider, connectionString);
        using var scope = services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        Assert.Same(context, store.GetType());
        Assert.Equal(providerName, store.Database.ProviderName);
        // The SQL Server provider rewrites the string it is given into its own keywords, so the database it names is what is compared.
        Assert.Contains(connectionString.Contains("tokens.db", StringComparison.Ordinal) ? "tokens.db" : "elsa_tokens", store.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    /// <summary>The platform's own connection is the default for another engine, as it is for every Elsa EF module.</summary>
    [Fact]
    public void Another_engine_without_a_connection_string_of_its_own_uses_the_shared_elsa_connection()
    {
        const string Shared = "Host=shared;Database=elsa;Username=elsa;Password=x";
        using var services = CreateProvider("PostgreSql", connectionString: null, sharedConnection: Shared);
        using var scope = services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        Assert.IsType<OpenIddictIdentityPostgreSqlDbContext>(store);
        Assert.Equal(Shared, store.Database.GetConnectionString());
    }

    /// <summary>SQLite is the single-node default, and its file is the store's own, not the shared connection's.</summary>
    [Fact]
    public void Nothing_set_keeps_the_sqlite_store_in_its_own_file_whatever_the_shared_connection_is()
    {
        using var services = CreateProvider(provider: null, connectionString: null, sharedConnection: "Host=shared;Database=elsa;Username=elsa;Password=x");
        using var scope = services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        Assert.Same(typeof(OpenIddictIdentityDbContext), store.GetType());
        Assert.Equal(EfProviderNames.Sqlite, store.Database.ProviderName);
        Assert.Equal(OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString, store.Database.GetConnectionString());
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void The_demo_in_memory_store_has_no_engine_to_select(string provider)
    {
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused", isDevelopmentOrDemo: true);
        using var scope = services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        Assert.Same(typeof(OpenIddictIdentityDbContext), store.GetType());
        Assert.True(store.Database.IsInMemory());
    }

    /// <summary>A provider the store cannot use fails the host's start, with the supported ones named, instead of the store's first use.</summary>
    [Theory]
    [InlineData("Oracle")]
    [InlineData("Cosmos")]
    public void An_engine_the_platform_does_not_support_fails_the_hosts_start_with_the_supported_ones_named(string provider)
    {
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused");

        var refusal = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Sqlite, SqlServer or PostgreSql", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>MySQL's provider cannot run OpenIddict's prune, so a store on it would grow without bound: refused at the start, and the reason given.</summary>
    [Fact]
    public void MySql_fails_the_hosts_start_because_its_provider_cannot_prune_the_store()
    {
        using var services = CreateProvider("MySql", connectionString: "Server=db;Database=elsa_tokens;User=elsa;Password=x");

        var refusal = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("token prune", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("postgres")]
    public void A_supported_or_unset_engine_passes_the_start(string? provider)
    {
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused");

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    /// <summary>The store moves off its per-node SQLite only when its own setting says so, so a platform on another engine is warned of.</summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task A_platform_on_another_engine_with_the_store_left_on_sqlite_is_warned_of(string platform)
    {
        var log = new CapturingLogger();
        using var services = CreateProvider(provider: null, connectionString: null, log: log, platformProvider: platform);

        await StartNoticesAsync(services);

        var warning = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("per-node SQLite", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"{Section}:Provider", warning.Message, StringComparison.Ordinal);
        Assert.Contains(platform, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Sqlite")]
    [InlineData(null, null)]
    [InlineData("PostgreSql", "PostgreSql")]
    public async Task No_warning_when_the_store_follows_the_platform_or_the_platform_is_sqlite(string? provider, string? platform)
    {
        var log = new CapturingLogger();
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused", log: log, platformProvider: platform);

        await StartNoticesAsync(services);

        Assert.DoesNotContain(log.Entries, entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>The demo store is in memory whatever engine is configured, so a configured engine is warned of, and its absence is not.</summary>
    [Theory]
    [InlineData("PostgreSql", true)]
    [InlineData(null, false)]
    public async Task The_demo_store_warns_of_an_engine_it_ignores(string? provider, bool warned)
    {
        var log = new CapturingLogger();
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused", isDevelopmentOrDemo: true, log: log);

        await StartNoticesAsync(services);

        var warnings = log.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(warned, warnings.Length == 1);
        if (warned)
            Assert.Contains("in-memory demo store", warnings[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// EF Core finds a migration by the context type it was scaffolded for, so an engine's migrations are applied only by its own
    /// context: each context lists exactly its own initial migration, and none lists another engine's.
    /// </summary>
    [Fact]
    public void Each_engines_context_has_its_own_migrations()
    {
        var migrations = Engines.Select(engine =>
        {
            using var services = CreateProvider(engine.Provider, engine.ConnectionString);
            using var scope = services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.GetMigrations().Single();
        }).ToArray();

        Assert.Equal(Engines.Length, migrations.Distinct().Count());
    }

    /// <summary>The migrations history table is the one the SQLite store's migrations already record themselves in, as every Elsa EF module's is named.</summary>
    [Fact]
    public void The_migrations_history_table_is_named_for_the_module_as_the_sqlite_stores_always_was()
    {
        Assert.Equal(OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable, EfMigrationsHistory.TableName(WorkbenchOpenIddictStoreProvider.Module));
    }

    private static async Task StartNoticesAsync(IServiceProvider services)
    {
        foreach (var notices in services.GetServices<IHostedService>().OfType<WorkbenchOpenIddictStoreNotices>())
            await notices.StartAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateProvider(
        string? provider,
        string? connectionString,
        string? sharedConnection = null,
        bool isDevelopmentOrDemo = false,
        CapturingLogger? log = null,
        string? platformProvider = null)
    {
        var settings = new Dictionary<string, string?> { [$"{Section}:IsDevelopmentOrDemo"] = isDevelopmentOrDemo.ToString() };
        if (provider is not null)
            settings[$"{Section}:Provider"] = provider;
        if (connectionString is not null)
            settings[$"{Section}:ConnectionString"] = connectionString;
        if (sharedConnection is not null)
            settings["ConnectionStrings:Elsa"] = sharedConnection;
        if (platformProvider is not null)
        {
            settings["Elsa:Persistence:DefaultResource"] = "primary";
            settings["Elsa:Persistence:Resources:primary:Provider"] = platformProvider;
            settings["Elsa:Persistence:Resources:primary:ConnectionName"] = "Elsa";
        }

        return WorkbenchOpenIddictTestHost.CreateProvider(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            configure: log is null ? null : services => services.AddSingleton<ILoggerFactory>(log));
    }
}
