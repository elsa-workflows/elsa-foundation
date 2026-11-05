using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>What only a server engine can show about the ETL design schema: its migrations, collations and registration.</summary>
public abstract class EtlDesignProviderTests(EtlNativeProviderFixture fixture)
{
    [SkippableFact]
    public async Task A_fresh_database_migrates_validates_and_matches_the_model()
    {
        await using var database = await EtlProviderDesignDatabase.CreateTypedAsync(fixture);
        await using var context = database.CreateContext();
        var expected = EfRelationalProviderBinding.ExpectedProviderName(database.Provider);

        await EfDatabaseMigrator.ApplyAsync(context, expected, EfMigratePolicy.Validate);

        Assert.Equal(expected, context.Database.ProviderName);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [SkippableFact]
    public async Task Key_columns_use_the_engines_binary_collation()
    {
        await using var database = await EtlProviderDesignDatabase.CreateTypedAsync(fixture);
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(database.Provider));

        foreach (var (table, column) in new[]
                 {
                     (EtlDesignEfModule.DraftTableName, "ScopeKey"), (EtlDesignEfModule.DraftTableName, "DefinitionKey"),
                     (EtlDesignEfModule.PublishedArtifactTableName, "ScopeKey"), (EtlDesignEfModule.PublishedArtifactTableName, "DefinitionKey")
                 })
            Assert.Equal(expected, await ScalarAsync(database, fixture.ColumnCollationSql(table, column)));
    }

    [SkippableFact]
    public async Task The_design_module_registers_the_engines_context()
    {
        await using var database = await EtlProviderDesignDatabase.CreateTypedAsync(fixture);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEtlDesignEntityFrameworkCore(new EtlDesignEntityFrameworkCoreOptions
        {
            Provider = database.Provider, ConnectionString = database.ConnectionString
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<EtlDesignDbContext>();

        await using (var expected = database.CreateContext())
            Assert.Equal(expected.GetType(), context.GetType());
        Assert.Equal(EfRelationalProviderBinding.ExpectedProviderName(database.Provider), context.Database.ProviderName);
        Assert.True(await context.Database.CanConnectAsync());
    }

    private static async Task<string?> ScalarAsync(EtlProviderDesignDatabase database, string sql)
    {
        await using var context = database.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() as string;
    }
}

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlDesignPostgreSqlProviderTests(EtlPostgreSqlProviderFixture fixture) : EtlDesignProviderTests(fixture);

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlDesignSqlServerProviderTests(EtlSqlServerProviderFixture fixture) : EtlDesignProviderTests(fixture);

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlDesignMySqlProviderTests(EtlMySqlProviderFixture fixture) : EtlDesignProviderTests(fixture);
