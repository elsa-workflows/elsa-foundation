using Elsa.Persistence.EntityFramework;
using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Theory]
    [InlineData("Oracle")]
    [InlineData("Cosmos")]
    public void An_engine_the_platform_does_not_support_is_refused_with_the_supported_ones_named(string provider)
    {
        using var services = CreateProvider(provider, connectionString: null, sharedConnection: "unused");
        using var scope = services.CreateScope();

        var refusal = Assert.Throws<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>());

        Assert.Contains("Sqlite, SqlServer, PostgreSql, or MySql", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>MySQL's provider cannot run OpenIddict's prune, so a store on it would grow without bound: refused, and the reason given.</summary>
    [Fact]
    public void MySql_is_refused_because_its_provider_cannot_prune_the_store()
    {
        using var services = CreateProvider("MySql", connectionString: "Server=db;Database=elsa_tokens;User=elsa;Password=x");
        using var scope = services.CreateScope();

        var refusal = Assert.Throws<NotSupportedException>(() => scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>());

        Assert.Contains("token prune", refusal.Message, StringComparison.Ordinal);
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

    private static ServiceProvider CreateProvider(
        string? provider,
        string? connectionString,
        string? sharedConnection = null,
        bool isDevelopmentOrDemo = false)
    {
        var settings = new Dictionary<string, string?> { [$"{Section}:IsDevelopmentOrDemo"] = isDevelopmentOrDemo.ToString() };
        if (provider is not null)
            settings[$"{Section}:Provider"] = provider;
        if (connectionString is not null)
            settings[$"{Section}:ConnectionString"] = connectionString;
        if (sharedConnection is not null)
            settings["ConnectionStrings:Elsa"] = sharedConnection;
        return WorkbenchOpenIddictTestHost.CreateProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }
}
