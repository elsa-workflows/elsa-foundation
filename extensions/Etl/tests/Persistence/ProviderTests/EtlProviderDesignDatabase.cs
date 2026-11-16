using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>A fresh ETL design database on a server engine, migrated as a host would migrate it.</summary>
internal sealed class EtlProviderDesignDatabase(EtlProviderDatabase database) : IEtlDesignTestDatabase
{
    internal string Provider => database.Provider;
    internal string ConnectionString => database.ConnectionString;

    public static async Task<IEtlDesignTestDatabase> CreateAsync(EtlNativeProviderFixture fixture) => await CreateTypedAsync(fixture);

    internal static async Task<EtlProviderDesignDatabase> CreateTypedAsync(EtlNativeProviderFixture fixture) =>
        await CreateTypedAsync(await fixture.CreateDatabaseAsync());

    internal static async Task<EtlProviderDesignDatabase> CreateTypedAsync(EtlProviderDatabase allocated, params IInterceptor[] interceptors)
    {
        var design = new EtlProviderDesignDatabase(allocated);
        try
        {
            await using var context = design.CreateContext(interceptors);
            await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(design.Provider));
            return design;
        }
        catch (Exception failure)
        {
            try { await design.DisposeAsync(); }
            catch (Exception cleanup) { failure.Data["EtlDatabaseCleanupFailure"] = cleanup.GetType().FullName; }
            throw;
        }
    }

    public EtlDesignDbContext CreateContext(params IInterceptor[] interceptors) => database.Provider switch
    {
        "PostgreSql" => Create<EtlDesignPostgreSqlDbContext>(interceptors),
        "SqlServer" => Create<EtlDesignSqlServerDbContext>(interceptors),
        "MySql" => Create<EtlDesignMySqlDbContext>(interceptors),
        _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "No ETL design context for this provider.")
    };

    private TContext Create<TContext>(IInterceptor[] interceptors) where TContext : EtlDesignDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        EfRelationalProviderBinding.UseMigrationsFrom(builder, database.Provider, database.ConnectionString,
            EtlDesignEfModule.HistoryTableName, typeof(TContext).Assembly);
        builder.AddInterceptors(interceptors);
        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
