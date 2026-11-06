using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>A fresh ETL runtime database on a server engine, migrated as a host would migrate it.</summary>
internal sealed class EtlProviderRuntimeDatabase(EtlProviderDatabase database) : IEtlRuntimeTestDatabase
{
    internal string Provider => database.Provider;
    internal string ConnectionString => database.ConnectionString;

    public static async Task<IEtlRuntimeTestDatabase> CreateAsync(EtlNativeProviderFixture fixture) => await CreateTypedAsync(fixture);

    internal static async Task<EtlProviderRuntimeDatabase> CreateTypedAsync(EtlNativeProviderFixture fixture) =>
        await CreateTypedAsync(await fixture.CreateDatabaseAsync());

    internal static async Task<EtlProviderRuntimeDatabase> CreateTypedAsync(EtlProviderDatabase allocated, params IInterceptor[] interceptors)
    {
        var runtime = new EtlProviderRuntimeDatabase(allocated);
        try
        {
            await using var context = runtime.CreateContext(interceptors);
            await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(runtime.Provider));
            return runtime;
        }
        catch (Exception failure)
        {
            try { await runtime.DisposeAsync(); }
            catch (Exception cleanup) { failure.Data["EtlDatabaseCleanupFailure"] = cleanup.GetType().FullName; }
            throw;
        }
    }

    public EtlRuntimeDbContext CreateContext(params IInterceptor[] interceptors) => database.Provider switch
    {
        "PostgreSql" => Create<EtlRuntimePostgreSqlDbContext>(interceptors),
        "SqlServer" => Create<EtlRuntimeSqlServerDbContext>(interceptors),
        "MySql" => Create<EtlRuntimeMySqlDbContext>(interceptors),
        _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "No ETL runtime context for this provider.")
    };

    private TContext Create<TContext>(IInterceptor[] interceptors) where TContext : EtlRuntimeDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        EfRelationalProviderBinding.UseMigrationsFrom(builder, database.Provider, database.ConnectionString,
            EtlRuntimeEfModule.HistoryTableName, typeof(TContext).Assembly);
        builder.AddInterceptors(interceptors);
        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
