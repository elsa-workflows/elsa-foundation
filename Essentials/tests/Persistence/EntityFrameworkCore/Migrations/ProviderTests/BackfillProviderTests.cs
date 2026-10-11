using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.BackfillDatabase;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// The post-finalization backfill (spec 186) on each server engine, over the synthetic family the SQLite suite proves
/// every mechanism on: the keyset selection over a text key and a composite one, a row's compare-and-set lost to a live
/// write, the finalization record's compare-and-set for the claim, the completion and its withdrawal, all depend on what
/// the engine translates and reports, which SQLite cannot speak for.
/// </summary>
public sealed class BackfillProviderTests
{
    [SkippableFact]
    public Task The_backfill_upgrades_verifies_records_audits_and_withdraws_on_postgresql() =>
        RunAsync("PostgreSql", (builder, connection) => builder.UseNpgsql(connection));

    [SkippableFact]
    public Task The_backfill_upgrades_verifies_records_audits_and_withdraws_on_sql_server() =>
        RunAsync("SqlServer", (builder, connection) => builder.UseSqlServer(connection));

    [SkippableFact]
    public Task The_backfill_upgrades_verifies_records_audits_and_withdraws_on_mysql() =>
        RunAsync("MySql", (builder, connection) => builder.UseMySQL(connection));

    /// <summary>
    /// FR-008 after FR-018 on PostgreSQL, where the claim on a withdrawal is a compare-and-set the engine's concurrency
    /// check decides: several workers racing for it after a withdrawal give one upgrader.
    /// </summary>
    [SkippableFact]
    public Task After_a_withdrawal_several_workers_racing_for_the_claim_give_one_upgrader_on_postgresql() =>
        ProviderDatabase.RunAsync("PostgreSql", async connection =>
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
            var database = new BackfillDatabase(builder => builder.UseNpgsql(connection));
            await database.CreateAsync(clock);
            await BackfillWithdrawalRace.RunAsync(database, clock, workers: 4);
        });

    private static Task RunAsync(string provider, Func<DbContextOptionsBuilder, string, DbContextOptionsBuilder> use) =>
        ProviderDatabase.RunAsync(provider, async connection =>
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
            var database = new BackfillDatabase(builder => use(builder, connection));
            await database.CreateAsync(clock);
            await database.SeedAsync(
                Order("o1", 10), Order("o2", 20), Order("o3", 30), Order("o4", 40), Order("o5", 50), Order("o6", 60), Order("o7", 70),
                Line("o1", 1), Line("o1", 2), Line("o2", 1), Line("o2", 2), Line("o3", 10),
                Receipt("hash-1", "2"));
            // A host alone in its fleet, as the in-process membership makes it: the settle condition holds once its own gate
            // has observed the version.
            var fleet = new FakeFleetState();
            var member = fleet.Add(new FakeMember("solo").Reading(BackfillFamily.Family, BackfillFamily.Chain));
            member.Observations = new EfSchemaFinalizationObservations();
            await using var host = new BackfillHost(
                database,
                new FakeFleet(fleet, member),
                new EfSchemaBackfillOptions { BatchSize = 2, BatchPause = TimeSpan.Zero },
                clock,
                observations: member.Observations);
            await host.ActivateAsync();
            var raced = false;
            host.Probe.BeforeWrite = async row =>
            {
                if (!raced && BackfillProbe.Key(row) == "BackfillOrderRow:o3")
                {
                    raced = true;
                    await host.SaveOrderAsync(new BackfillFamily.Order("o3", 99, "USD"));
                }
            };

            await host.RunOnceAsync();

            var snapshot = await database.SnapshotAsync();
            Assert.All(snapshot.Where(row => !row.StartsWith("receipt", StringComparison.Ordinal)), row => Assert.Contains(" 2 r2 ", row));
            Assert.Contains($"order o3 2 r2 USD {BackfillFamily.OrderContent("o3", 99, "USD", "2")}", snapshot);
            Assert.Contains($"line o3/10 2 r2 {BackfillFamily.LineContent("sku-o3-10", "piece", "2")}", snapshot);
            Assert.Equal(11, host.Probe.WrittenRows.Count);
            var complete = await database.RecordAsync();
            Assert.Equal("2", complete.Finish!.CompletionVersion);
            Assert.Null(complete.Finish.Run);

            await database.SeedAsync(Order("straggler", 5));
            clock.Advance(TimeSpan.FromHours(1));
            await host.RunOnceAsync();

            var withdrawn = await database.RecordAsync();
            Assert.Null(withdrawn.Finish);
            Assert.Equal("2", withdrawn.FinalizedVersion);
            Assert.Contains($"'{BackfillFamily.OrdersTable}': 1", withdrawn.FinishHistory[^1].Reason);

            await host.RunOnceAsync();

            Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
            Assert.Equal(EfSchemaBackfillState.Complete, host.Status.State);
        });
}
