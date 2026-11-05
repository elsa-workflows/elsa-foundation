using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsNameSchemaTests
{
    [Fact]
    public async Task Old_format_writes_continue_until_case_preserving_format_is_finalized()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var gates = new EfSchemaFinalizationGates();
        await using var services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .UseApplicationServiceProvider(services);
        EfSchemaWriteGateInterceptor.EnsureAdded(builder);
        await using var context = new SecretsSqliteDbContext(builder.Options);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
        var families = EfSchemaModuleFamilies.For("Secrets", typeof(SecretsDbContext).Assembly);
        var gate = new EfSchemaModuleGate(families, null, new EfSchemaFinalizationObservations(), new EfSchemaFinalizationOptions());
        await gate.ActivateAsync(context);
        gates.Register(typeof(SecretsDbContext), gate);
        var repository = new EfSecretRepository(context);

        Assert.True(await repository.TryAddAsync(new Secret { TenantId = "tenant", Name = "legacy" }));
        var legacy = await context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("1.0.0", legacy.SchemaVersion);
        var document = SecretDocument.Parse(legacy.Payload);
        // The preceding reader's projection identity invariant is preserved while v1 can still be written.
        Assert.Equal(document.Secret.Name, document.NormalizedName);
        // post-migrate constructs a bare context: repairing projections must not upgrade its payload stamp.
        var bareOptions = new DbContextOptionsBuilder<SecretsSqliteDbContext>().UseSqlite(connection).Options;
        await using (var bare = new SecretsSqliteDbContext(bareOptions))
        {
            var stale = await bare.Secrets.SingleAsync();
            stale.NameSearchKey = "stale";
            await bare.SaveChangesAsync();
            bare.ChangeTracker.Clear();
            Assert.Equal(1, await SecretsProjectionContract.ReindexAsync(bare));
            Assert.Equal("1.0.0", (await bare.Secrets.AsNoTracking().SingleAsync()).SchemaVersion);
        }
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(async () =>
            await repository.TryAddAsync(new Secret { TenantId = "tenant", Name = "DbConnectionString" }));
        Assert.Equal(1, await context.Secrets.CountAsync());

        await FinalizeAsync(context, gate);
        Assert.True(await repository.TryAddAsync(new Secret { TenantId = "tenant", Name = "DbConnectionString" }));
        Assert.Equal("legacy", (await repository.FindAsync("tenant", "legacy"))!.Name);
        Assert.Equal("DbConnectionString", (await repository.FindAsync("tenant", "DBCONNECTIONSTRING"))!.Name);
        var current = await context.Secrets.AsNoTracking().SingleAsync(row => row.NormalizedName == "dbconnectionstring");
        Assert.Equal("2.0.0", current.SchemaVersion);
        var oldReader = EfSchemaChain.For(families.DeclarationOf(SecretsEfModule.SchemaFamily) with { CurrentVersion = "1.0.0", Upcasters = [] });
        Assert.Throws<EfSchemaVersionSkewException>(() => oldReader.EnsureReadable(current.SchemaVersion));
        Assert.False(await SecretsProjectionContract.HasLegacyProjectionsAsync(context));
    }

    internal static async Task FinalizeAsync(SecretsDbContext context, EfSchemaModuleGate gate)
    {
        var store = new EfSchemaFinalizationStore(context);
        var record = (await store.FindAsync(SecretsEfModule.SchemaFamily))!;
        if (record.FinalizedVersion == SecretsEfModule.SchemaVersion)
            return;
        var member = new SchemaFinalizationMember("owned-test-host", "test-incarnation");
        var chain = SecretsEfModule.Chain.ReadableVersions;
        var intent = await store.RecordIntentAsync(SecretsEfModule.SchemaFamily, record.Revision, SecretsEfModule.SchemaVersion, chain, member);
        await store.CommitIntentAsync(SecretsEfModule.SchemaFamily, intent.Record.Revision, chain, member);
        await gate.RefreshAsync(context);
    }
}
