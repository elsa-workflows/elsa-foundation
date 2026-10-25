using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Rules.Tests.RuleStoreTestFixture;

namespace Elsa.Rules.Tests;

public sealed class RuleDatabaseClockTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Fact]
    public async Task Actual_sqlite_clock_has_UTC_millisecond_precision_and_advances_inside_a_transaction()
    {
        await using var db = fixture.Contexts.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var before = await ReadDateAsync(db);
        var ticks = await db.ReadUtcTicksAsync(default);
        var after = await ReadDateAsync(db);
        Assert.InRange(ticks, before, after);
        Assert.Equal(0, ticks % TimeSpan.TicksPerMillisecond);
        // This tests the database clock's transaction semantics, not a timing/performance threshold.
        await Task.Delay(25);
        Assert.True(await db.ReadUtcTicksAsync(default) > ticks);
    }

    [Fact]
    public async Task Claim_renew_and_release_preserve_the_real_schema_write_gate()
    {
        var gates = new EfSchemaFinalizationGates();
        using var services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<RulesSqliteDbContext>().UseApplicationServiceProvider(services);
        EfModuleBinding.For(typeof(RulesDbContext)).Apply(builder, services, EfProviderNames.Sqlite,
            $"Data Source={fixture.DatabasePath};Pooling=False", connectionName: null);
        var contexts = new GateContextFactory(builder.Options);
        var gate = new EfSchemaModuleGate(EfSchemaModuleFamilies.ForContext(typeof(RulesSqliteDbContext))!,
            null, new EfSchemaFinalizationObservations(), new EfSchemaFinalizationOptions());
        await using (var db = contexts.CreateDbContext()) await gate.ActivateAsync(db);
        gates.Register(typeof(RulesSqliteDbContext), gate);
        var store = new EfRuleRunStore(contexts, new FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready), TimeSpan.FromDays(1));
        var invocation = NewInvocation();
        await store.AdmitAsync(new(invocation));
        var request = new RuleClaimRequest("schema-worker", [invocation.ExpectedImplementation], [1], [1], [1]);
        var lease = Assert.IsType<RuleWorkToken>((await store.ClaimWorkAsync(request)).Token).Lease;
        await store.AdmitAsync(new(NewInvocation())); // An eligible unleased row for the refused claim.
        var before = await ReadRunsAsync(contexts);

        // Stage a newer build's finalized schema through the real finalization-store protocol.
        await using (var db = contexts.CreateDbContext())
        {
            var finalization = new EfSchemaFinalizationStore(db);
            var member = new SchemaFinalizationMember("newer-host", "test-incarnation");
            var record = (await finalization.FindAsync(RulesEfModule.SchemaFamily))!;
            var intent = await finalization.RecordIntentAsync(RulesEfModule.SchemaFamily, record.Revision,
                "2.0.0", ["1.0.0", "2.0.0"], member);
            Assert.True(intent.Applied);
            Assert.True((await finalization.CommitIntentAsync(RulesEfModule.SchemaFamily, intent.Record.Revision,
                ["1.0.0", "2.0.0"], member)).Applied);
            await gate.RefreshAsync(db);
        }
        Assert.True(gate.StateOf(RulesEfModule.SchemaFamily)!.WritesRefused);
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => store.ClaimWorkAsync(request).AsTask());
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => store.RenewAsync(lease).AsTask());
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => store.ReleaseAsync(lease).AsTask());
        Assert.Equal(before, await ReadRunsAsync(contexts));
    }

    private static async Task<long> ReadDateAsync(RulesDbContext db) => DateTimeOffset.ParseExact(
        await db.Database.SqlQueryRaw<string>("SELECT strftime('%Y-%m-%dT%H:%M:%fZ', 'now') AS Value").SingleAsync(),
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcTicks;

    private static async Task<string> ReadRunsAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        await using var db = await contexts.CreateDbContextAsync();
        return JsonSerializer.Serialize(await db.Runs.AsNoTracking().OrderBy(row => row.RunKey).ToArrayAsync());
    }

    private sealed class GateContextFactory(DbContextOptions<RulesSqliteDbContext> options) : IDbContextFactory<RulesDbContext>
    {
        public RulesDbContext CreateDbContext() => new RulesSqliteDbContext(options);
    }
}
