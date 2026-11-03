using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>A fresh ETL design database on a server engine, migrated as a host would migrate it (spec 1202).</summary>
internal sealed class EtlProviderDesignDatabase(EtlProviderDatabase database) : IEtlDesignTestDatabase
{
    internal string Provider => database.Provider;
    internal string ConnectionString => database.ConnectionString;

    public static async Task<IEtlDesignTestDatabase> CreateAsync(EtlNativeProviderFixture fixture) => await CreateTypedAsync(fixture);

    internal static async Task<EtlProviderDesignDatabase> CreateTypedAsync(EtlNativeProviderFixture fixture)
    {
        var design = new EtlProviderDesignDatabase(await fixture.CreateDatabaseAsync());
        await using var context = design.CreateContext();
        await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(design.Provider));
        return design;
    }

    public EtlDesignDbContext CreateContext() => database.Provider switch
    {
        "PostgreSql" => Create<EtlDesignPostgreSqlDbContext>(),
        "SqlServer" => Create<EtlDesignSqlServerDbContext>(),
        "MySql" => Create<EtlDesignMySqlDbContext>(),
        _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "No ETL design context for this provider.")
    };

    private TContext Create<TContext>() where TContext : EtlDesignDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        EfRelationalProviderBinding.UseMigrationsFrom(builder, database.Provider, database.ConnectionString,
            EtlDesignEfModule.HistoryTableName, typeof(TContext).Assembly);
        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
