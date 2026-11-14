using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Tests;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>What only a server engine can show about the ETL runtime schema: its migrations, collations and registration.</summary>
public abstract class EtlRuntimeProviderTests(EtlNativeProviderFixture fixture)
{
    [SkippableFact]
    public async Task A_fresh_runtime_database_migrates_validates_and_matches_the_model()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        await using var context = database.CreateContext();
        var expected = EfRelationalProviderBinding.ExpectedProviderName(database.Provider);

        await EfDatabaseMigrator.ApplyAsync(context, expected, EfMigratePolicy.Validate);

        Assert.Equal(expected, context.Database.ProviderName);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [SkippableFact]
    public async Task Runtime_key_columns_use_the_engines_binary_collation_and_hashed_run_keys()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(database.Provider));

        foreach (var (table, column) in new[]
                 {
                     (EtlRuntimeEfModule.RunTableName, "ScopeKey"), (EtlRuntimeEfModule.RunTableName, "RunKey"),
                     (EtlRuntimeEfModule.ConnectorPartitionTableName, "RunKey"), (EtlRuntimeEfModule.ConnectorBatchTableName, "BatchId")
                 })
            Assert.Equal(expected, await ScalarAsync(database, fixture.ColumnCollationSql(table, column)));
        await using var context = database.CreateContext();
        Assert.Equal(EtlRuntimeEfModule.HashLength, context.RunKeyOf(new string('r', EtlRuntimeEfModule.IdentityMaximumLength)).Length);
        Assert.NotEqual(context.RunKeyOf("Run-A"), context.RunKeyOf("run-a"));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runtime_only_registration_selects_the_engine_and_preserves_scoped_capabilities(bool pooling)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEtlRuntimeEntityFrameworkCore(new EtlRuntimeEntityFrameworkCoreOptions
        {
            Provider = database.Provider, ConnectionString = database.ConnectionString, Pooling = pooling
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var admission = EtlRuntimeFixtures.Admission("registered-store");
        IEtlRuntimeStore first;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            await using (var expected = database.CreateContext())
                Assert.Equal(expected.GetType(), context.GetType());
            Assert.Equal(EfRelationalProviderBinding.ExpectedProviderName(database.Provider), context.Database.ProviderName);
            first = scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            Assert.Same(first, scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>());
            Assert.Same(first, scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeInspector>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlDefinitionStore>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlDefinitionStoreV2>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlArtifactCompiler>());
            Assert.Equal(EtlStoreStatus.Applied, (await first.AdmitAsync(admission)).Status);
            Assert.Equal(EtlStoreStatus.Replay, (await first.AdmitAsync(admission)).Status);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            Assert.NotSame(first, store);
            var retained = Assert.IsType<EtlRunView>(await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
            Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation), EtlRuntimeIdentity.InvocationFingerprint(retained.Invocation));
        }
    }

    private static async Task<string?> ScalarAsync(EtlProviderRuntimeDatabase database, string sql)
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
public sealed class EtlRuntimePostgreSqlProviderTests(EtlPostgreSqlProviderFixture fixture) : EtlRuntimeProviderTests(fixture);

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlRuntimeSqlServerProviderTests(EtlSqlServerProviderFixture fixture) : EtlRuntimeProviderTests(fixture);

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlRuntimeMySqlProviderTests(EtlMySqlProviderFixture fixture) : EtlRuntimeProviderTests(fixture);
