using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.BackfillDatabase;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 186, FR-008 after FR-018, a race every engine must settle the same way, so the provider tests compile it in: the
/// family's completion withdrawn with no claim standing, and several hosts that all find the withdrawal and read the
/// finalization record before any of them writes it. Exactly one wins the claim on the withdrawal, upgrades every row and
/// records the completion; the others lose the compare-and-set and read none of the rows, having found its claim, or,
/// where the claimant finished first, the completion it recorded. Before the claim could be held on a withdrawal, a
/// withdrawn completion left nothing to claim in, and every one of them upgraded the same rows.
/// </summary>
public static class BackfillWithdrawalRace
{
    public static async Task RunAsync(BackfillDatabase database, TimeProvider clock, int workers)
    {
        await database.SeedAsync(
            Order("o1", 10), Order("o2", 20), Order("o3", 30), Order("o4", 40), Order("o5", 50),
            Line("o1", 1), Line("o1", 2), Line("o2", 1));
        var fleet = new FakeFleetState();
        var hosts = new Dictionary<BackfillHost, string>();
        try
        {
            for (var index = 0; index < workers; index++)
            {
                var member = fleet.Add(new FakeMember($"host-{index}").Reading(BackfillFamily.Family, BackfillFamily.Chain));
                member.Observations = new EfSchemaFinalizationObservations();
                var host = new BackfillHost(
                    database,
                    new FakeFleet(fleet, member),
                    new EfSchemaBackfillOptions { BatchSize = 2, BatchPause = TimeSpan.Zero },
                    clock,
                    observations: member.Observations);
                hosts[host] = member.HostId;
                await host.ActivateAsync();
            }

            await using (var context = database.Context())
            {
                var store = new EfSchemaFinalizationStore(context, clock);
                var record = (await store.FindAsync(BackfillFamily.Family))!;
                Assert.True((await store.WithdrawCompletionAsync(BackfillFamily.Family, record.Revision, new SchemaFinalizationMember("auditor", "a"), "a straggler", worker: null)).Applied);
            }

            using var allRead = new CountdownEvent(workers);
            foreach (var host in hosts.Keys)
            {
                await host.RefreshAsync();
                var waited = false;
                host.Probe.BeforeCommand = _ =>
                {
                    if (waited)
                        return Task.CompletedTask;
                    waited = true;
                    allRead.Signal();
                    Assert.True(allRead.Wait(TimeSpan.FromSeconds(30)), "Every worker must read the record before any of them writes it.");
                    return Task.CompletedTask;
                };
            }

            await Task.WhenAll(hosts.Keys.Select(host => Task.Run(() => host.RunOnceAsync())));

            var upgrader = Assert.Single(hosts.Keys, host => host.Probe.AskedRows.Count > 0);
            Assert.Equal(8, upgrader.Probe.WrittenRows.Count);
            Assert.Equal(8, upgrader.Probe.WrittenRows.Distinct().Count());
            Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 r2 ", row));
            var completed = await database.RecordAsync();
            Assert.Equal(("2", hosts[upgrader]), (completed.Finish!.CompletionVersion, completed.Finish.RecordedBy.Member!.HostId));
            Assert.Null(completed.BackfillRun);
            Assert.Single(completed.FinishHistory, entry => entry is { Transition: SchemaFinishTransition.Completed, Version: "2" });
        }
        finally
        {
            foreach (var host in hosts.Keys)
                await host.DisposeAsync();
        }
    }
}
