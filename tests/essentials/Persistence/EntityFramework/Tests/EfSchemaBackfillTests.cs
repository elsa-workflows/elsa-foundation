using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.BackfillDatabase;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The post-finalization backfill (spec 186) on the synthetic family at versions 1 and 2, on a file database every host
/// of a test shares, with a fleet the test controls and a clock it moves. Every mechanism is checked in both directions
/// (FR-024), and every "nothing happened" from the record and the rows themselves, since a backfill that recorded
/// completion over a row it had not upgraded would look exactly like one that works.
/// </summary>
public sealed class EfSchemaBackfillTests : IAsyncLifetime
{
    private readonly TemporarySqliteDatabase file = new("schema-backfill");
    private static readonly TimeSpan AuditInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RepairableInterval = TimeSpan.FromMinutes(5);
    private static readonly string RecordTable = EfSchemaFinalization.RecordTableName(BackfillFamily.HistoryModule);

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeFleetState fleet = new();
    private readonly List<BackfillHost> hosts = [];
    private readonly Dictionary<string, FakeMember> members = new(StringComparer.Ordinal);
    private readonly BackfillDatabase database;

    public EfSchemaBackfillTests() => database = new BackfillDatabase(builder => builder.UseSqlite(file.ConnectionString));

    public Task InitializeAsync() => database.CreateAsync(clock);

    public async Task DisposeAsync()
    {
        try
        {
            foreach (var host in hosts)
                await host.DisposeAsync();
        }
        finally
        {
            await file.DisposeAsync();
        }
    }

    /// <summary>User Story 1, acceptance 1 and 2; SC-001 and FR-014.</summary>
    [Fact]
    public async Task A_run_upgrades_every_row_below_the_finalized_version_reads_each_back_unchanged_and_records_completion()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var before = await DomainValuesAsync(host);

        await host.RunOnceAsync();

        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
        Assert.Equal(before, await DomainValuesAsync(host));
        Assert.Equal(8, host.Probe.WrittenRows.Count);
        Assert.Equal(8, host.Probe.WrittenRows.Distinct().Count());
        var record = await database.RecordAsync();
        Assert.Equal("2", record.Finish!.CompletionVersion);
        Assert.NotNull(record.Finish.VerificationStartedAt);
        Assert.True(record.Finish.VerificationEndedAt >= record.Finish.VerificationStartedAt);
        Assert.Equal("host-a", record.Finish.RecordedBy.Member!.HostId);
        Assert.Equal((SchemaFinishTransition.Completed, "2"), (record.FinishHistory[^1].Transition, record.FinishHistory[^1].Version));
        Assert.Equal(EfSchemaBackfillState.Complete, host.Status.State);
        Assert.Equal("2", host.Gate.ObservedRecordOf(BackfillFamily.Family)!.Value.Record.Finish!.CompletionVersion);
    }

    /// <summary>User Story 1, acceptance 3; FR-002: nothing is written at a version before this host observes it as finalized.</summary>
    [Fact]
    public async Task Before_the_version_is_finalized_the_backfill_writes_nothing_and_once_it_is_it_upgrades()
    {
        await SeedFamilyAsync();
        var lagging = fleet.Add(new FakeMember("host-old").Reading(BackfillFamily.Family, "1"));
        lagging.Published = true;
        var host = await HostAsync("host-a", claim: TimeSpan.Zero);
        var before = await database.SnapshotAsync();

        await host.RunOnceAsync();

        Assert.Equal("1", host.Gate.StateOf(BackfillFamily.Family)!.WriteVersion);
        Assert.Equal(before, await database.SnapshotAsync());
        Assert.Empty(host.Probe.AskedRows);
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);

        lagging.Reading(BackfillFamily.Family, "1", "2");
        lagging.Observed[BackfillFamily.Family] = "2";
        await host.EvaluateAsync();
        await host.RunOnceAsync();

        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-007, both ways: a row at the target is skipped, and a rerun, on this host or another, writes no row and leaves the
    /// proof as it was. This host's rerun writes nothing at all; the other host's first round audits the completion under
    /// its claim (FR-008), and its claim and the release of it, once the audit has found nothing, are all it writes.
    /// </summary>
    [Fact]
    public async Task A_row_already_at_the_target_is_skipped_and_a_rerun_on_any_host_repeats_only_reads()
    {
        await database.SeedAsync(Order("o1", 10), Order("o2", 20, "2"), Line("o1", 1, "2"), Line("o1", 2));
        var host = await HostAsync("host-a");

        await host.RunOnceAsync();

        Assert.Equal(["BackfillLineRow:o1/2", "BackfillOrderRow:o1"], host.Probe.WrittenRows.Order());
        var after = await database.SnapshotAsync();
        var record = await database.RecordAsync();

        await host.RunOnceAsync();
        Assert.Equal(record.Revision, (await database.RecordAsync()).Revision);
        var other = await HostAsync("host-b");
        var recordWrites = CountRecordWrites(other);
        await other.RunOnceAsync();

        Assert.Equal(2, host.Probe.WrittenRows.Count);
        Assert.Empty(other.Probe.WrittenRows);
        Assert.Equal(after, await database.SnapshotAsync());
        var rerun = await database.RecordAsync();
        AssertProofUnchanged(record, rerun);
        Assert.Equal(2, recordWrites());
        Assert.Equal(record.Revision + 2, rerun.Revision);
        Assert.Null(rerun.BackfillRun);
        Assert.NotNull(other.Status.LastAuditAt);
    }

    /// <summary>
    /// FR-012, both ways: the upgrade pass runs at once, but verification waits until every counted member reports observing
    /// the target, and then for the settle margin, and the member it waits for is named on the status.
    /// </summary>
    [Fact]
    public async Task Verification_waits_for_every_counted_member_to_observe_the_target_and_then_for_the_settle_margin()
    {
        await SeedFamilyAsync();
        var peer = fleet.Add(new FakeMember("host-peer").Reading(BackfillFamily.Family, "1", "2"));
        peer.Published = true;
        peer.Observed[BackfillFamily.Family] = "1";
        var host = await HostAsync("host-a", margin: TimeSpan.FromSeconds(35));

        await host.RunOnceAsync();

        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Equal(EfSchemaBackfillState.Settling, host.Status.State);
        Assert.Contains("host-peer", Assert.Single(host.Status.SettleWaitingFor));

        peer.Observed[BackfillFamily.Family] = "2";
        await host.RunOnceAsync();
        clock.Advance(TimeSpan.FromSeconds(34));
        await host.RunOnceAsync();

        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Empty(host.Status.SettleWaitingFor);

        clock.Advance(TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-012: the configured settle margin may lengthen the fleet's, for a shell drain longer than the default, and never
    /// shortens it: a member's deactivation reaches the report one heartbeat later and its shell's last writes can outlast
    /// the drain, so a shorter value is raised to what the fleet needs to see a member stop writing.
    /// </summary>
    [Theory]
    [InlineData(1, 35)]
    [InlineData(0, 35)]
    [InlineData(35, 35)]
    [InlineData(60, 60)]
    public async Task A_configured_settle_margin_never_shortens_the_fleets_and_may_lengthen_it(int configuredSeconds, int expectedSeconds)
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", margin: TimeSpan.FromSeconds(35), configuredMargin: TimeSpan.FromSeconds(configuredSeconds));

        await host.RunOnceAsync();
        clock.Advance(TimeSpan.FromSeconds(expectedSeconds - 1));
        await host.RunOnceAsync();

        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);

        clock.Advance(TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-013 and FR-024, both ways: a row written below the target behind the upgrade pass is found by verification, which
    /// records nothing and starts again; the pass after it, finding nothing, records completion.
    /// </summary>
    [Fact]
    public async Task A_verification_pass_that_finds_a_row_records_nothing_and_the_pass_after_it_records_completion()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", verificationPasses: 1);
        var late = false;
        host.Probe.AfterWrite = async row =>
        {
            // Behind the upgrade pass's cursor: "a-late" sorts before every order it will select after this one.
            if (!late && row.Entity == typeof(BackfillOrderRow))
            {
                late = true;
                await database.SeedAsync(Order("a-late", 5));
            }
        };

        await host.RunOnceAsync();

        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Contains("BackfillOrderRow:a-late", host.Probe.WrittenRows);
        Assert.Equal(EfSchemaBackfillState.Verifying, host.Status.State);

        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
    }

    /// <summary>
    /// User Story 4; SC-004, FR-018 and FR-019, both ways: an audit over a family with no row below its completion changes
    /// nothing of the proof, only the claim it ran under; a straggler written after completion by a writer still at 1 is
    /// rewritten, reported, and withdraws the completion but not the finalized version, until a new verification pass
    /// records it again.
    /// </summary>
    [Fact]
    public async Task A_straggler_after_completion_is_rewritten_and_withdraws_the_completion_until_verification_records_it_again()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        var complete = await database.RecordAsync();

        clock.Advance(TimeSpan.FromHours(1));
        await host.RunOnceAsync();
        AssertProofUnchanged(complete, await database.RecordAsync());
        Assert.NotNull(host.Status.LastAuditAt);

        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(TimeSpan.FromMinutes(59));
        await host.RunOnceAsync();
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);

        clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunOnceAsync();

        var withdrawn = await database.RecordAsync();
        Assert.Null(withdrawn.Finish);
        Assert.Equal("2", withdrawn.FinalizedVersion);
        var entry = withdrawn.FinishHistory[^1];
        Assert.Equal((SchemaFinishTransition.Withdrawn, "2"), (entry.Transition, entry.Version));
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1", entry.Reason);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order straggler 2 r2 EUR", StringComparison.Ordinal));
        Assert.Null(host.Gate.ObservedRecordOf(BackfillFamily.Family)!.Value.Record.Finish);

        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>User Story 5; SC-005, FR-010a, FR-011a and FR-011b, both ways.</summary>
    [Fact]
    public async Task Content_addressed_rows_are_never_rewritten_and_while_any_is_below_the_target_the_family_is_never_complete()
    {
        await SeedFamilyAsync();
        await database.SeedAsync(Receipt("hash-1"), Receipt("hash-2"));
        var host = await HostAsync("host-a");
        var receipts = Receipts(await database.SnapshotAsync());

        await host.RunOnceAsync();

        Assert.Equal(receipts, Receipts(await database.SnapshotAsync()));
        Assert.DoesNotContain(host.Probe.AskedRows, row => row.StartsWith(nameof(BackfillReceiptRow), StringComparison.Ordinal));
        Assert.All((await database.SnapshotAsync()).Where(row => !row.StartsWith("receipt", StringComparison.Ordinal)), row => Assert.Contains(" 2 ", row));
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        var blocker = Assert.Single(host.Status.Blockers);
        Assert.Equal((EfSchemaBackfillBlockerKind.ContentAddressed, BackfillFamily.ReceiptsTable, 2L), (blocker.Kind, blocker.Table, blocker.Count));
        Assert.True(host.Status.BlockedByContentAddressedRows);
        // A blocked run leaves the family until it is surveyed again, so it lets go of its claim (FR-008).
        Assert.Null((await database.RecordAsync()).BackfillRun);

        // A receipt a version-2 writer produced sits beside them: once the old ones are gone, the family completes when it
        // is next surveyed, an audit interval on (FR-023).
        await using (var context = host.Context())
            await context.Receipts.Where(row => row.SchemaVersion == "1").ExecuteDeleteAsync();
        await database.SeedAsync(Receipt("hash-3", "2"));
        clock.Advance(AuditInterval);
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>FR-006, both ways: skew and corruption are reported, never rewritten or skipped silently, and block completion until resolved.</summary>
    [Fact]
    public async Task Rows_with_a_missing_or_unreadable_stamp_and_rows_that_fail_to_upcast_are_reported_and_block_completion()
    {
        await SeedFamilyAsync();
        var corrupt = Order("corrupt", 1);
        corrupt.ContentJson = "{ not json";
        var unstamped = Order("unstamped", 2);
        unstamped.SchemaVersion = null!;
        await database.SeedAsync(corrupt, unstamped, Order("retired", 3, "0"));
        var host = await HostAsync("host-a");

        await host.RunOnceAsync();

        // The audit of the completion standing at 1 cannot place the unstamped and retired rows at or after it, so it
        // withdraws it (FR-018); the run towards 2 is blocked by them.
        var record = await database.RecordAsync();
        Assert.Null(record.Finish);
        Assert.Contains($"table '{BackfillFamily.OrdersTable}': 2 (with a stamp this host cannot read)", record.FinishHistory[^1].Reason);
        Assert.Equal(EfSchemaBackfillState.Blocked, host.Status.State);
        var skew = Assert.Single(host.Status.Blockers, blocker => blocker.Kind == EfSchemaBackfillBlockerKind.Skew);
        Assert.Equal(2, skew.Count);
        var corruption = Assert.Single(host.Status.Blockers, blocker => blocker.Kind == EfSchemaBackfillBlockerKind.Corruption);
        Assert.Contains("Id=corrupt", corruption.Detail);
        Assert.Contains(BackfillFamily.Family, corruption.Detail);
        Assert.Contains("order corrupt 1 r1 - { not json", await database.SnapshotAsync());
        Assert.Contains("order o1 2 r2 EUR", string.Join("\n", await database.SnapshotAsync()));

        await using (var context = host.Context())
            await context.Orders.Where(row => row.Id == "corrupt" || row.Id == "unstamped" || row.Id == "retired").ExecuteDeleteAsync();
        clock.Advance(AuditInterval);
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>FR-004: a family whose rows are below the target but that names no rewriter says so, and is never recorded complete.</summary>
    [Fact]
    public async Task A_family_that_names_no_rewriter_reports_it_and_is_never_recorded_complete()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", families: BackfillFamily.Families(rewriter: false));
        var before = await database.SnapshotAsync();

        await host.RunOnceAsync();

        Assert.Equal(before, await database.SnapshotAsync());
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Equal(EfSchemaBackfillBlockerKind.NoRewriter, Assert.Single(host.Status.Blockers).Kind);
    }

    /// <summary>
    /// FR-004: a rewriter the shell cannot construct, because a service it takes is not composed there, blocks completion
    /// and says why, rather than failing every round with nothing on the status.
    /// </summary>
    [Fact]
    public async Task A_rewriter_the_shell_cannot_construct_is_reported_and_the_family_is_never_recorded_complete()
    {
        await SeedFamilyAsync();
        var declaration = BackfillFamily.Declaration() with { Rewriter = typeof(RewriterWithAnUncomposedService) };
        var host = await HostAsync("host-a", families: EfSchemaModuleFamilies.FromDeclarations(BackfillFamily.Module, [declaration]));
        var before = await database.SnapshotAsync();

        await host.RunOnceAsync();

        Assert.Equal(before, await database.SnapshotAsync());
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Equal(EfSchemaBackfillState.Blocked, host.Status.State);
        Assert.All(host.Status.Blockers, blocker => Assert.Equal(EfSchemaBackfillBlockerKind.NoRewriter, blocker.Kind));
        Assert.Contains(nameof(RewriterWithAnUncomposedService), host.Status.Blockers[0].Detail);
    }

    /// <summary>
    /// User Story 3, acceptance 1; SC-002: a host killed within a batch, before or after a row's write, or between two
    /// batches, loses nothing. Its claim keeps another host off only until it expires, and the other host finishes with the
    /// very table an uninterrupted run leaves.
    /// </summary>
    [Theory]
    [InlineData("before a write")]
    [InlineData("after a write")]
    [InlineData("between batches")]
    public async Task A_run_killed_mid_way_is_finished_by_another_host_with_the_table_an_uninterrupted_run_leaves(string crash)
    {
        var reference = await UninterruptedSnapshotAsync();
        await SeedFamilyAsync();
        var first = await HostAsync("host-a");
        var writes = 0;
        var killed = false;
        // Lines are upgraded first, then orders, two rows a batch: o1/1 and o1/2, then o2/1; then o1 and o2, o3 and o4, o5.
        first.Probe.BeforeWrite = _ => crash switch
        {
            // o2/1: the first row of the second batch, before any of it is written.
            "between batches" when writes == 2 => Kill(),
            // o2: the second row of a batch whose first row, o1, is written.
            "before a write" when writes == 4 => Kill(),
            _ => Task.CompletedTask
        };
        // o1 is written, and the host dies before o2 of the same batch is read.
        first.Probe.AfterWrite = _ => ++writes == 4 && crash == "after a write" ? Kill() : Task.CompletedTask;
        // A killed host sends nothing more, so it cannot release its claim as a host whose round merely failed does.
        first.Probe.BeforeCommand = _ => killed ? throw new HostKilledException() : Task.CompletedTask;

        await Assert.ThrowsAsync<HostKilledException>(() => first.RunOnceAsync());

        var second = await HostAsync("host-b");
        await second.RunOnceAsync();
        Assert.Empty(second.Probe.WrittenRows);
        Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, second.Status.State);
        Assert.Equal("host-a", second.Status.ClaimedBy!.Member.HostId);

        clock.Advance(TimeSpan.FromMinutes(2));
        await second.RunOnceAsync();

        Assert.Equal(reference, await database.SnapshotAsync());
        Assert.Empty(first.Probe.WrittenRows.Intersect(second.Probe.WrittenRows));
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);

        Task Kill()
        {
            killed = true;
            throw new HostKilledException();
        }
    }

    /// <summary>
    /// User Story 3, acceptance 2; FR-008: two hosts running at once with no claim to keep them apart, both reading the same
    /// row before either writes it, write each row once, lose the compare-and-set harmlessly, and record completion once.
    /// </summary>
    [Fact]
    public async Task Two_hosts_running_at_once_without_a_claim_write_each_row_once_and_record_completion_once()
    {
        await SeedFamilyAsync();
        var first = await HostAsync("host-a", claim: TimeSpan.Zero);
        var second = await HostAsync("host-b", claim: TimeSpan.Zero);
        using var bothRead = new CountdownEvent(2);
        foreach (var host in new[] { first, second })
        {
            host.Probe.BeforeWrite = row =>
            {
                if (BackfillProbe.Key(row) == "BackfillOrderRow:o1" && !bothRead.IsSet)
                {
                    bothRead.Signal();
                    Assert.True(bothRead.Wait(TimeSpan.FromSeconds(20)), "Both hosts must read o1 before either writes it.");
                }

                return Task.CompletedTask;
            };
        }

        await Task.WhenAll(Task.Run(() => first.RunOnceAsync()), Task.Run(() => second.RunOnceAsync()));

        Assert.Empty(first.Probe.WrittenRows.Intersect(second.Probe.WrittenRows));
        Assert.Equal(8, first.Probe.WrittenRows.Count + second.Probe.WrittenRows.Count);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 r2 ", row));
        var record = await database.RecordAsync();
        Assert.Single(record.FinishHistory, entry => entry is { Transition: SchemaFinishTransition.Completed, Version: "2" });
    }

    /// <summary>FR-008: with its claim, a second host running at the same time leaves the run to the first, and reads nothing.</summary>
    [Fact]
    public async Task A_claimed_run_is_left_alone_by_a_second_host_until_the_claim_ends()
    {
        await SeedFamilyAsync();
        var first = await HostAsync("host-a");
        var second = await HostAsync("host-b");
        first.Probe.BeforeWrite = async _ =>
        {
            if (first.Probe.WrittenRows.Count == 0)
                await second.RunOnceAsync();
        };

        await first.RunOnceAsync();

        Assert.Empty(second.Probe.AskedRows);
        Assert.Equal(8, first.Probe.WrittenRows.Count);
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Null((await database.RecordAsync()).Finish!.Run);
    }

    /// <summary>
    /// FR-008, both ways, the case a survey taken before the claim would hide: a worker that finds the family claimed
    /// elsewhere, here while the claimant waits out its settle margin, sends nothing to the family's tables, neither a
    /// survey nor a verification pass nor its first audit, and sends nothing at all while the claim holds; once the claim
    /// has expired, it takes the family over and finishes it.
    /// </summary>
    [Fact]
    public async Task A_worker_that_finds_the_family_claimed_elsewhere_reads_none_of_its_rows_until_the_claim_expires()
    {
        var margin = TimeSpan.FromSeconds(35);
        await SeedFamilyAsync();
        var claimant = await HostAsync("host-a", margin: margin);
        var other = await HostAsync("host-b", margin: margin);
        await claimant.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillState.Settling, claimant.Status.State);
        var selections = CountSelections(other);

        await other.RunOnceAsync();

        Assert.Equal(0, selections());
        Assert.Empty(other.Probe.AskedRows);
        Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, other.Status.State);
        Assert.Equal("host-a", other.Status.ClaimedBy!.Member.HostId);
        Assert.Null(other.Status.LastAuditAt);

        var commands = CountCommands(other);
        clock.Advance(TimeSpan.FromSeconds(30));
        await other.RunOnceAsync();
        Assert.Equal(0, commands());

        // The claimant stops; its claim expires a minute after it was taken.
        clock.Advance(TimeSpan.FromSeconds(31));
        selections = CountSelections(other);
        await other.RunOnceAsync();

        Assert.True(selections() > 0);
        Assert.Equal(EfSchemaBackfillState.Settling, other.Status.State);

        clock.Advance(margin);
        await other.RunOnceAsync();

        Assert.Equal(("2", "host-b"), CompletedBy(await database.RecordAsync()));
        Assert.Empty(other.Probe.WrittenRows);
    }

    /// <summary>
    /// FR-008 after FR-018, both ways, the case that cost N-way contention: the worker whose audit withdraws the completion
    /// keeps its claim, now on the withdrawal, and goes on rewriting what it found, while every other worker that finds the
    /// withdrawal, its gate already showing no completion, leaves the family alone rather than rewrite the same rows. The
    /// claimant then records the completion again, which drops its claim.
    /// </summary>
    [Fact]
    public async Task After_a_withdrawal_the_worker_that_made_it_upgrades_and_the_others_read_none_of_the_rows()
    {
        await SeedFamilyAsync();
        var auditor = await HostAsync("host-a");
        await auditor.RunOnceAsync();
        BackfillHost[] others = [await HostAsync("host-b"), await HostAsync("host-c"), await HostAsync("host-d")];
        await database.SeedAsync(Order("s1", 1), Order("s2", 2), Order("s3", 3));
        clock.Advance(AuditInterval);
        var othersRan = false;
        auditor.Probe.BeforeWrite = async _ =>
        {
            if (othersRan)
                return;
            othersRan = true;
            Assert.Null((await database.RecordAsync()).Finish);
            foreach (var other in others)
            {
                await other.RefreshAsync();
                await other.RunOnceAsync();
            }
        };

        await auditor.RunOnceAsync();

        Assert.True(othersRan);
        Assert.All(others, other =>
        {
            Assert.Empty(other.Probe.AskedRows);
            Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, other.Status.State);
            Assert.Equal("host-a", other.Status.ClaimedBy!.Member.HostId);
        });
        Assert.Equal(["BackfillOrderRow:s1", "BackfillOrderRow:s2", "BackfillOrderRow:s3"], auditor.Probe.WrittenRows.Skip(8));
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);

        await auditor.RunOnceAsync();

        var record = await database.RecordAsync();
        Assert.Equal(("2", "host-a"), CompletedBy(record));
        Assert.Null(record.BackfillRun);
        Assert.Single(record.FinishHistory, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
    }

    /// <summary>
    /// FR-008 after FR-018, the race: a completion withdrawn with no claim standing, found by several workers whose rounds
    /// all read the finalization record before any of them writes it. One wins the claim on the withdrawal and upgrades
    /// every row; the others lose the compare-and-set, find its claim, and read none of the rows.
    /// </summary>
    [Fact]
    public Task After_a_withdrawal_several_workers_racing_for_the_claim_give_one_upgrader() =>
        BackfillWithdrawalRace.RunAsync(database, clock, workers: 4);

    /// <summary>
    /// FR-008 with FR-014, the case that looks like success: a worker whose claim lapsed while it verified, and that
    /// another worker took over, records no completion although its own pass found nothing, so it finishes nothing it no
    /// longer owns; the claimant verifies after its own settle margin and records it.
    /// </summary>
    [Fact]
    public async Task A_worker_whose_claim_was_taken_over_while_it_verified_records_nothing_and_the_claimant_records()
    {
        var margin = TimeSpan.FromSeconds(35);
        await SeedFamilyAsync();
        var stale = await HostAsync("host-a", margin: margin);
        var taker = await HostAsync("host-b", margin: margin);
        await stale.RunOnceAsync();
        clock.Advance(margin);
        var takenOver = false;
        stale.Probe.BeforeCommand = async text =>
        {
            // At the last table of its verification pass, after its last renewal: its claim lapses, and another worker
            // takes the family over.
            if (takenOver || stale.Status.State != EfSchemaBackfillState.Verifying || !text.Contains(BackfillFamily.ReceiptsTable, StringComparison.Ordinal))
                return;
            takenOver = true;
            clock.Advance(TimeSpan.FromMinutes(2));
            await taker.RunOnceAsync();
            Assert.Equal(EfSchemaBackfillState.Settling, taker.Status.State);
        };

        await stale.RunOnceAsync();

        Assert.True(takenOver);
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
        Assert.Equal(EfSchemaBackfillState.Verifying, stale.Status.State);
        Assert.Contains("claimed by host-b", stale.Status.Detail);

        clock.Advance(margin);
        await taker.RunOnceAsync();

        Assert.Equal(("2", "host-b"), CompletedBy(await database.RecordAsync()));
    }

    /// <summary>
    /// FR-008 with spec 183's FR-007, both ways, the case a claim that merely expires would hide: a member that lapses from
    /// the fleet mid-run stops before the next row it would rewrite, releases its claim, and reads nothing while it stays
    /// lapsed, so another member takes the family over at once rather than a claim period later.
    /// </summary>
    [Fact]
    public async Task A_member_that_lapses_mid_run_stops_releases_its_claim_and_another_takes_the_family_over_at_once()
    {
        await SeedFamilyAsync();
        var lapsing = await HostAsync("host-a");
        var other = await HostAsync("host-b");
        lapsing.Probe.AfterWrite = _ =>
        {
            members["host-a"].Lapsed = true;
            return Task.CompletedTask;
        };

        await lapsing.RunOnceAsync();

        // The row it was rewriting, and no more: the read before the next row finds the lapse.
        Assert.Single(lapsing.Probe.WrittenRows);
        Assert.Equal(EfSchemaBackfillState.Idle, lapsing.Status.State);
        Assert.Contains("lapsed", lapsing.Status.Detail);
        Assert.Null((await database.RecordAsync()).BackfillRun);
        var selections = CountSelections(lapsing);
        await lapsing.RunOnceAsync();
        Assert.Equal(0, selections());

        await other.RunOnceAsync();

        Assert.Equal(7, other.Probe.WrittenRows.Count);
        Assert.Empty(lapsing.Probe.WrittenRows.Intersect(other.Probe.WrittenRows));
        Assert.Equal(("2", "host-b"), CompletedBy(await database.RecordAsync()));
    }

    /// <summary>
    /// FR-008 with spec 183's FR-007, the way back: a member that lapsed and stopped mid-run, once it rejoins as a new
    /// incarnation, claims the family again and finishes the run, and records the completion under that incarnation.
    /// </summary>
    [Fact]
    public async Task A_member_that_lapses_and_rejoins_as_a_new_incarnation_resumes_backfilling()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var member = members["host-a"];
        host.Probe.AfterWrite = _ =>
        {
            member.Lapsed = true;
            return Task.CompletedTask;
        };
        await host.RunOnceAsync();
        Assert.Single(host.Probe.WrittenRows);
        Assert.Null((await database.RecordAsync()).BackfillRun);

        host.Probe.AfterWrite = null;
        var lapsedIncarnation = member.Incarnation;
        member.Incarnation = Guid.NewGuid().ToString("N");
        member.Lapsed = false;
        await host.RunOnceAsync();

        Assert.Equal(8, host.Probe.WrittenRows.Count);
        Assert.Equal(8, host.Probe.WrittenRows.Distinct().Count());
        var record = await database.RecordAsync();
        Assert.Equal(("2", "host-a"), CompletedBy(record));
        Assert.Equal(member.Incarnation, record.Finish!.RecordedBy.Member!.Incarnation);
        Assert.NotEqual(lapsedIncarnation, member.Incarnation);
    }

    /// <summary>
    /// FR-008, the release that cannot land: a member that lapses, and whose release loses its compare-and-set on every
    /// attempt, fails that round with its claim still standing, and releases it the next round it is still lapsed. It is
    /// retried, not left to expire.
    /// </summary>
    [Fact]
    public async Task A_release_that_loses_every_compare_and_set_is_made_again_the_next_round()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var losing = false;
        host.Probe.AfterWrite = _ =>
        {
            members["host-a"].Lapsed = true;
            losing = true;
            return Task.CompletedTask;
        };
        host.Probe.BeforeCommand = async text =>
        {
            if (!losing || !IsRecordWrite(text))
                return;
            // Another writer changes the record between each attempt's read and its write.
            await using var context = database.Context();
            await context.Set<EfSchemaFinalizationRecordRow>().ExecuteUpdateAsync(row => row.SetProperty(record => record.Revision, record => record.Revision + 1));
        };

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunOnceAsync());

        Assert.Contains("attempts to release the backfill claim", failed.Message);
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);

        losing = false;
        host.Probe.AfterWrite = null;
        await host.RunOnceAsync();

        Assert.Null((await database.RecordAsync()).BackfillRun);
        Assert.Single(host.Probe.WrittenRows);
        Assert.Equal(EfSchemaBackfillState.Idle, host.Status.State);
    }

    /// <summary>
    /// FR-008, the case that held the family for good: a round that fails with something other than a stop, here the
    /// database going away under a row's write, lets go of its claim before the failure goes on, so a worker whose rounds
    /// keep failing never holds the other workers off; and the module's other families still run that round, which then
    /// fails with that failure.
    /// </summary>
    [Fact]
    public async Task A_round_that_fails_releases_its_claim_and_the_modules_other_families_still_run()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", families: EfSchemaModuleFamilies.FromDeclarations(BackfillFamily.Module, [BackfillFamily.Declaration(), TablelessFamily]));
        host.Probe.BeforeWrite = _ => throw new InvalidOperationException("The database went away.");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunOnceAsync());

        Assert.Equal("The database went away.", failed.Message);
        var record = await database.RecordAsync();
        Assert.Null(record.BackfillRun);
        Assert.Equal("1", record.Finish!.CompletionVersion);
        Assert.Equal("2", (await RecordOfAsync(TablelessFamily.Name)).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-008 with the round's isolation, both families failing: each lets go of its claim, and the round fails with both
    /// failures, neither hiding the other.
    /// </summary>
    [Fact]
    public async Task When_two_families_fail_in_one_round_both_release_their_claims_and_the_round_reports_both()
    {
        await SeedFamilyAsync();
        var orders = BackfillFamily.Declaration() with { Entities = [typeof(BackfillOrderRow), typeof(BackfillReceiptRow)] };
        var lines = BackfillFamily.Declaration() with { Name = "BackfillLines", Entities = [typeof(BackfillLineRow)], ContentAddressed = [] };
        var host = await HostAsync("host-a", families: EfSchemaModuleFamilies.FromDeclarations(BackfillFamily.Module, [orders, lines]));
        host.Probe.BeforeWrite = row => throw new InvalidOperationException($"The database went away under {row.Entity.Name}.");

        var failed = await Assert.ThrowsAsync<AggregateException>(() => host.RunOnceAsync());

        Assert.Equal(
            ["The database went away under BackfillOrderRow.", "The database went away under BackfillLineRow."],
            failed.InnerExceptions.Select(failure => failure.Message));
        Assert.Null((await RecordOfAsync(orders.Name)).BackfillRun);
        Assert.Null((await RecordOfAsync(lines.Name)).BackfillRun);
    }

    /// <summary>
    /// FR-008, the release that fails after a failed round: the round's own failure is the one it reports, and the release's
    /// failure is logged as a warning rather than thrown over it; the claim then expires on its own.
    /// </summary>
    [Fact]
    public async Task A_release_that_fails_after_a_failed_round_is_logged_and_the_rounds_own_failure_is_reported()
    {
        await SeedFamilyAsync();
        var logger = new WarningLogger();
        var host = await HostAsync("host-a", logger: logger);
        var failing = false;
        host.Probe.BeforeWrite = _ =>
        {
            failing = true;
            throw new InvalidOperationException("The database went away.");
        };
        host.Probe.BeforeCommand = text => failing && IsRecordWrite(text) ? throw new TimeoutException("The release timed out.") : Task.CompletedTask;

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunOnceAsync());

        Assert.Equal("The database went away.", failed.Message);
        var warning = Assert.Single(logger.Warnings, entry => entry.Message.Contains("could not release its claim", StringComparison.Ordinal));
        Assert.NotNull(warning.Exception);
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);
    }

    /// <summary>
    /// FR-008, the early ways out of a round: a worker that kept its claim while its run settled, and whose host then comes
    /// to refuse every write to the family, since a version it cannot read was finalized elsewhere, lets go of the claim on
    /// that round rather than keep the family from the hosts that can still work on it.
    /// </summary>
    [Fact]
    public async Task A_claim_kept_while_settling_is_released_once_the_host_refuses_every_write_to_the_family()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", margin: TimeSpan.FromSeconds(35));
        await host.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillState.Settling, host.Status.State);
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);
        await WithStoreAsync(async store =>
        {
            string[] newer = ["1", "2", "3"];
            var member = new SchemaFinalizationMember("host-newer", "n");
            var intended = await store.RecordIntentAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, "3", newer, member);
            await store.CommitIntentAsync(BackfillFamily.Family, intended.Record.Revision, newer, member);
        });
        await host.RefreshAsync();
        Assert.True(host.Gate.StateOf(BackfillFamily.Family)!.WritesRefused);

        await host.RunOnceAsync();

        Assert.Null((await database.RecordAsync()).BackfillRun);
        Assert.Contains("refuses every write", host.Status.Detail);
    }

    /// <summary>
    /// FR-008, the stop that held the family until its claim expired: a round cancelled mid-run, as a stopping shell
    /// cancels it, still lets go of its claim, on a token of its own.
    /// </summary>
    [Fact]
    public async Task A_round_cancelled_mid_run_releases_its_claim()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        using var stopping = new CancellationTokenSource();
        host.Probe.AfterWrite = async _ => await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RunOnceAsync(stopping.Token));

        Assert.Single(host.Probe.WrittenRows);
        Assert.Null((await database.RecordAsync()).BackfillRun);
    }

    /// <summary>
    /// FR-008 with FR-018, the case a renewal inside the withdrawal's compare-and-set would lose: an audit whose evidence
    /// takes longer than a third of the claim period, on every attempt, still withdraws the completion, since the claim is
    /// renewed before the compare-and-set and between its attempts, and never written inside one.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_whose_evidence_spans_a_renewal_interval_still_lands()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        string? first = null;
        string? previous = null;
        var slowScans = 0;
        host.Probe.BeforeCommand = text =>
        {
            if (FamilyTables.FirstOrDefault(table => text.Contains(table, StringComparison.Ordinal)) is not { } table)
                return Task.CompletedTask;
            first ??= table;
            // Each selection over the family's tables takes longer than a third of the one-minute claim.
            if (table == first && previous != first && slowScans++ < 3)
                clock.Advance(TimeSpan.FromSeconds(25));
            previous = table;
            return Task.CompletedTask;
        };

        await host.RunOnceAsync();

        var record = await database.RecordAsync();
        Assert.Null(record.Finish);
        var withdrawal = Assert.Single(record.FinishHistory, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.Equal("host-a", withdrawal.Actor.Member!.HostId);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order straggler 2 r2", StringComparison.Ordinal));
    }

    /// <summary>
    /// FR-008, the case a stalled worker would hide: a worker whose claim lapses mid-batch, and that another worker takes
    /// over, rewrites no further row of that batch, since the read it takes before each row finds the other's claim.
    /// </summary>
    [Fact]
    public async Task A_worker_whose_claim_is_taken_over_mid_batch_rewrites_no_further_row()
    {
        await SeedFamilyAsync();
        var stalled = await HostAsync("host-a");
        stalled.Probe.AfterWrite = async _ =>
        {
            if (stalled.Probe.WrittenRows.Count != 1)
                return;
            clock.Advance(TimeSpan.FromMinutes(2));
            await TakeClaimAsync("2");
        };

        await stalled.RunOnceAsync();

        Assert.Single(stalled.Probe.WrittenRows);
        Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, stalled.Status.State);
        Assert.Equal("worker-b", (await database.RecordAsync()).BackfillRun!.Worker);
    }

    /// <summary>
    /// FR-008 with FR-018, the case a long audit would hide: an audit whose selection outlasts its claim, which another
    /// worker takes over meanwhile, stops at its next renewal, before it counts another table, and so neither withdraws the
    /// completion nor rewrites the straggler; the worker that holds the claim does that.
    /// </summary>
    [Fact]
    public async Task An_audit_that_outlasts_its_claim_and_loses_it_stops_before_the_next_table()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        string? scanning = null;
        var scannedAfterwards = new List<string>();
        host.Probe.BeforeCommand = async text =>
        {
            if (FamilyTables.FirstOrDefault(table => text.Contains(table, StringComparison.Ordinal)) is not { } table)
                return;
            if (scanning is not null)
            {
                if (table != scanning)
                    scannedAfterwards.Add(table);
                return;
            }

            // The audit's first table takes longer than its claim, and another worker takes the family over meanwhile.
            scanning = table;
            clock.Advance(TimeSpan.FromMinutes(2));
            await TakeClaimAsync("2");
        };

        await host.RunOnceAsync();

        Assert.NotNull(scanning);
        Assert.Empty(scannedAfterwards);
        var record = await database.RecordAsync();
        Assert.Equal("2", record.Finish!.CompletionVersion);
        Assert.Equal("worker-b", record.BackfillRun!.Worker);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order straggler 1 r1", StringComparison.Ordinal));
        Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, host.Status.State);
    }

    /// <summary>
    /// FR-008 with FR-018, both ways: a worker running without a claim, as one whose claim duration is zero does, refuses to
    /// withdraw a completion while another worker's live claim stands, so it never moves that claim onto its withdrawal; it
    /// reads the record again and gives up, leaving the straggler to the claimant. Once that claim has expired, it withdraws.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_over_another_workers_live_claim_is_refused_and_one_after_it_expires_is_made()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", claim: TimeSpan.Zero);
        await host.RunOnceAsync();
        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        await TakeClaimAsync("2");

        await host.RunOnceAsync();

        var refused = await database.RecordAsync();
        Assert.Equal("2", refused.Finish!.CompletionVersion);
        Assert.DoesNotContain(refused.FinishHistory, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.Equal("worker-b", refused.BackfillRun!.Worker);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order straggler 1 r1", StringComparison.Ordinal));
        Assert.Equal(EfSchemaBackfillState.ClaimedElsewhere, host.Status.State);
        Assert.Contains("withdraws nothing", host.Status.Detail);

        clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunOnceAsync();

        var withdrawn = (await database.RecordAsync()).FinishHistory.Single(entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.Equal("host-a", withdrawn.Actor.Member!.HostId);
        Assert.Null(withdrawn.Run);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order straggler 2 r2", StringComparison.Ordinal));
    }

    /// <summary>
    /// User Story 3, acceptance 3; FR-007: a live write between the backfill's read and its write wins the compare-and-set;
    /// the backfill reads the row again, finds it at 2, and leaves the live write as it is.
    /// </summary>
    [Fact]
    public async Task A_live_write_between_the_backfills_read_and_write_wins_and_the_backfill_rereads_the_row_at_2()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var raced = false;
        host.Probe.BeforeWrite = async row =>
        {
            if (!raced && BackfillProbe.Key(row) == "BackfillOrderRow:o2")
            {
                raced = true;
                await host.SaveOrderAsync(new BackfillFamily.Order("o2", 99, "USD"));
            }
        };

        await host.RunOnceAsync();

        Assert.DoesNotContain("BackfillOrderRow:o2", host.Probe.WrittenRows);
        Assert.Equal(2, host.Probe.AskedRows.Count(row => row == "BackfillOrderRow:o2"));
        Assert.Contains($"order o2 2 r2 USD {BackfillFamily.OrderContent("o2", 99, "USD", "2")}", await database.SnapshotAsync());
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>FR-021: the status names the target and the progress while a run is going, and the members settle waits for.</summary>
    [Fact]
    public async Task The_status_names_the_target_and_counts_the_rows_rewritten_while_the_run_goes()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        EfSchemaBackfillStatus? during = null;
        host.Probe.AfterWrite = _ =>
        {
            if (host.Probe.WrittenRows.Count == 3)
                during = host.Status;
            return Task.CompletedTask;
        };

        await host.RunOnceAsync();

        Assert.Equal((EfSchemaBackfillState.Upgrading, "2"), (during!.State, during.TargetVersion));
        Assert.Equal(2, during.RowsRewritten);
        var status = Assert.Single(await host.Gate.ReadStatusAsync(host.Context()), family => family.Family == BackfillFamily.Family);
        Assert.Equal((EfSchemaBackfillState.Complete, 8L), (status.Backfill!.State, status.Backfill.RowsRewritten));
        Assert.Equal("2", status.Finish!.CompletionVersion);
    }

    /// <summary>Edge case "Several versions finalized at once": one run upgrades every row below the newest, through the whole chain.</summary>
    [Fact]
    public async Task When_several_versions_finalize_at_once_one_run_upgrades_every_row_through_the_whole_chain()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", current: "3");

        await host.RunOnceAsync();

        var snapshot = await database.SnapshotAsync();
        Assert.All(snapshot, row => Assert.Contains(" 3 r2 ", row));
        Assert.Contains($"order o1 3 r2 EUR {BackfillFamily.OrderContent("o1", 10, "EUR", "3")}", snapshot);
        Assert.Equal("3", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-003, Edge case "Finalization advances during a run": rows are written at this host's write version at the moment
    /// of each write, so the ones after the advance are at the newer version; the run completes for the target its
    /// verification proves, and the next run for the newer one.
    /// </summary>
    [Fact]
    public async Task When_finalization_advances_during_a_run_later_rows_are_written_at_the_newer_version_and_each_run_completes_its_own_target()
    {
        await SeedFamilyAsync();
        await WithStoreAsync(async store =>
            await store.PlaceHoldAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, "3", "canary of 3", "ops", ["1", "2", "3"]));
        var host = await HostAsync("host-a", current: "3");
        Assert.Equal("2", host.Gate.StateOf(BackfillFamily.Family)!.WriteVersion);
        host.Probe.AfterWrite = async _ =>
        {
            if (host.Probe.WrittenRows.Count == 3)
            {
                await WithStoreAsync(async store =>
                    await store.ReleaseHoldAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, "3", "ops"));
                await host.EvaluateAsync();
            }
        };

        await host.RunOnceAsync();

        var snapshot = await database.SnapshotAsync();
        Assert.Equal(3, snapshot.Count(row => row.Contains(" 2 r2 ", StringComparison.Ordinal)));
        Assert.Equal(5, snapshot.Count(row => row.Contains(" 3 r2 ", StringComparison.Ordinal)));
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);

        await host.RunOnceAsync();

        Assert.All(await database.SnapshotAsync(), row => Assert.Matches(@"^\w+ \S+ 3 r\d", row));
        Assert.Equal("3", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-018, both ways within one round: a host towards a newer finalized version whose audit meets a row below the
    /// completion that stands withdraws it, naming the table and the count, before it records the newer one; the rows
    /// between the two versions are not stragglers and withdraw nothing.
    /// </summary>
    [Fact]
    public async Task A_run_towards_a_newer_version_that_meets_a_row_below_the_standing_completion_withdraws_it_before_recording_the_newer_one()
    {
        await SeedFamilyAsync();
        var previous = await HostAsync("host-previous");
        await previous.RunOnceAsync();
        members["host-previous"].Live = false;
        await database.SeedAsync(Order("straggler", 5));
        var host = await HostAsync("host-a", current: "3");

        await host.RunOnceAsync();

        var history = (await database.RecordAsync()).FinishHistory;
        Assert.Equal((SchemaFinishTransition.Withdrawn, "2"), (history[^2].Transition, history[^2].Version));
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1 (to be rewritten)", history[^2].Reason);
        Assert.Single(history, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.Equal((SchemaFinishTransition.Completed, "3"), (history[^1].Transition, history[^1].Version));
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 3 ", row));
    }

    /// <summary>
    /// FR-018, the case a crash would hide: the audit, the upgrade pass and the verification pass each withdraw the
    /// completion before they rewrite the first row they find below it, so a host that dies before that rewrite leaves the
    /// straggler reported, not under a completion that still stands. The row itself is left for the next run.
    /// </summary>
    [Theory]
    [InlineData("the audit")]
    [InlineData("the upgrade pass")]
    [InlineData("the verification pass")]
    public async Task A_pass_that_dies_before_rewriting_a_straggler_has_already_withdrawn_the_completion(string pass)
    {
        await SeedFamilyAsync();
        var host = await CompleteAtTwoThenAsync(pass == "the audit" ? "2" : "3");
        var seeded = false;
        Task SeedStraggler()
        {
            seeded = true;
            // After every order the pass has already selected, so it meets the row later in the same pass.
            return database.SeedAsync(Order("z-straggler", 5));
        }

        switch (pass)
        {
            case "the audit":
                await SeedStraggler();
                clock.Advance(AuditInterval);
                break;
            case "the upgrade pass":
                host.Probe.AfterWrite = row => !seeded && row.Entity == typeof(BackfillOrderRow) ? SeedStraggler() : Task.CompletedTask;
                break;
            case "the verification pass":
                host.Probe.BeforeCommand = text => !seeded && host.Status.State == EfSchemaBackfillState.Verifying && text.Contains(BackfillFamily.OrdersTable, StringComparison.Ordinal)
                    ? SeedStraggler()
                    : Task.CompletedTask;
                break;
        }

        host.Probe.BeforeWrite = row => BackfillProbe.Key(row) == "BackfillOrderRow:z-straggler" ? throw new HostKilledException() : Task.CompletedTask;

        await Assert.ThrowsAsync<HostKilledException>(() => host.RunOnceAsync());

        var record = await database.RecordAsync();
        Assert.Null(record.Finish);
        var withdrawal = record.FinishHistory[^1];
        Assert.Equal((SchemaFinishTransition.Withdrawn, "2"), (withdrawal.Transition, withdrawal.Version));
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1", withdrawal.Reason);
        Assert.Contains(await database.SnapshotAsync(), row => row.StartsWith("order z-straggler 1 r1", StringComparison.Ordinal));
    }

    /// <summary>
    /// FR-013 and FR-014, the case that looks like success: a verification pass that finds nothing, but during which
    /// another worker withdrew the completion over a straggler it found, records nothing, and the pass after it does. The
    /// other worker withdraws once this one's claim has lapsed, since no worker withdraws while another's claim holds.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_that_lands_during_a_verification_pass_keeps_it_from_recording_completion()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var withdrawn = false;
        host.Probe.BeforeCommand = async text =>
        {
            if (withdrawn || host.Status.State != EfSchemaBackfillState.Verifying || !text.Contains(BackfillFamily.OrdersTable, StringComparison.Ordinal))
                return;
            withdrawn = true;
            clock.Advance(TimeSpan.FromMinutes(2));
            await WithStoreAsync(async store =>
                await store.WithdrawCompletionAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, HostB, "host-b found a straggler", worker: null));
        };

        await host.RunOnceAsync();

        Assert.True(withdrawn);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
        var record = await database.RecordAsync();
        Assert.Null(record.Finish);
        Assert.Equal((SchemaFinishTransition.Withdrawn, "1"), (record.FinishHistory[^1].Transition, record.FinishHistory[^1].Version));
        Assert.Equal(EfSchemaBackfillState.Verifying, host.Status.State);
        Assert.Contains("withdrawn after the settle margin began", host.Status.Detail);

        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-018: a standing completion is audited on its interval even while this host's target is ahead of it, so a
    /// straggler below it is reported by a host whose own run cannot make progress, here for want of a rewriter.
    /// </summary>
    [Fact]
    public async Task A_standing_completion_is_audited_while_the_hosts_target_is_ahead_of_it()
    {
        await SeedFamilyAsync();
        var previous = await HostAsync("host-previous");
        await previous.RunOnceAsync();
        members["host-previous"].Live = false;
        await database.SeedAsync(Order("straggler", 5));
        var host = await HostAsync("host-a", families: BackfillFamily.Families("3", rewriter: false));

        await host.RunOnceAsync();

        var record = await database.RecordAsync();
        Assert.Null(record.Finish);
        Assert.Equal("3", record.FinalizedVersion);
        Assert.Equal((SchemaFinishTransition.Withdrawn, "2"), (record.FinishHistory[^1].Transition, record.FinishHistory[^1].Version));
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1 (with no rewriter to upgrade them)", record.FinishHistory[^1].Reason);
        Assert.Equal(EfSchemaBackfillBlockerKind.NoRewriter, Assert.Single(host.Status.Blockers).Kind);
        Assert.NotNull(host.Status.LastAuditAt);
    }

    /// <summary>
    /// FR-012 after FR-018: once a completion is withdrawn, verification waits a full settle margin again before it may
    /// record one, however long ago the settle condition first held.
    /// </summary>
    [Fact]
    public async Task After_a_withdrawal_verification_waits_a_full_settle_margin_again()
    {
        var margin = TimeSpan.FromSeconds(35);
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", margin: margin);
        await host.RunOnceAsync();
        clock.Advance(margin);
        await host.RunOnceAsync();
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);

        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        await host.RunOnceAsync();
        Assert.Null((await database.RecordAsync()).Finish);

        await host.RunOnceAsync();
        clock.Advance(margin - TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Null((await database.RecordAsync()).Finish);
        Assert.Equal(EfSchemaBackfillState.Settling, host.Status.State);

        clock.Advance(TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// FR-012 after FR-018, across workers, the case a worker's own memory would hide: a worker whose settle condition has
    /// held for longer than the margin, and that did not make the withdrawal, still waits a full margin after another
    /// worker withdraws the completion, since it finds the withdrawal in the finish history past where its margin began.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_by_another_worker_restarts_the_settle_margin_of_a_worker_that_did_not_make_it()
    {
        var margin = TimeSpan.FromSeconds(35);
        await SeedFamilyAsync();
        // Without a claim, so both settle at once: the margin must restart whatever the claim does (FR-008).
        var withdrawer = await HostAsync("host-a", margin: margin, claim: TimeSpan.Zero);
        var other = await HostAsync("host-b", margin: margin, claim: TimeSpan.Zero);
        await withdrawer.RunOnceAsync();
        await other.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillState.Settling, other.Status.State);
        clock.Advance(margin);
        await withdrawer.RunOnceAsync();
        Assert.Equal(("2", "host-a"), CompletedBy(await database.RecordAsync()));

        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        await withdrawer.RunOnceAsync();
        var withdrawal = (await database.RecordAsync()).FinishHistory[^1];
        Assert.Equal((SchemaFinishTransition.Withdrawn, "host-a"), (withdrawal.Transition, withdrawal.Actor.Member!.HostId));

        await other.RunOnceAsync();
        clock.Advance(margin - TimeSpan.FromSeconds(1));
        await other.RunOnceAsync();

        Assert.Null((await database.RecordAsync()).Finish);
        Assert.Equal(EfSchemaBackfillState.Settling, other.Status.State);

        clock.Advance(TimeSpan.FromSeconds(1));
        await other.RunOnceAsync();

        Assert.Equal(("2", "host-b"), CompletedBy(await database.RecordAsync()));
    }

    /// <summary>
    /// The same on the claimed path: a worker that settles under its claim, and whose settle condition has held for the
    /// whole margin, waits a full margin again after a withdrawal it did not make, here written by a writer that keeps no
    /// claim once this worker's had lapsed, as the store allows; the worker claims the family again and records the
    /// completion only after the new margin.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_another_writer_makes_restarts_the_settle_margin_of_the_worker_that_holds_the_claim()
    {
        var margin = TimeSpan.FromSeconds(35);
        await SeedFamilyAsync();
        var claimant = await HostAsync("host-a", margin: margin);
        await claimant.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillState.Settling, claimant.Status.State);
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        await WithStoreAsync(async store =>
            await store.WithdrawCompletionAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, HostB, "host-b found a straggler", worker: null));
        Assert.Null((await database.RecordAsync()).BackfillRun);

        await claimant.RunOnceAsync();

        Assert.Null((await database.RecordAsync()).Finish);
        Assert.Equal(EfSchemaBackfillState.Settling, claimant.Status.State);
        Assert.Equal("host-a", (await database.RecordAsync()).BackfillRun!.Member.HostId);

        clock.Advance(margin);
        await claimant.RunOnceAsync();

        Assert.Equal(("2", "host-a"), CompletedBy(await database.RecordAsync()));
    }

    /// <summary>
    /// FR-018, both ways across a race: an audit withdraws only a completion that rows below it contradict when it
    /// withdraws. Here another worker rewrote the straggler the audit counted and recorded the completion again before the
    /// audit's withdrawal landed, so that completion stands; the audit's own withdrawal of the one it examined is the
    /// other direction, proven above.
    /// </summary>
    [Fact]
    public async Task An_audit_does_not_withdraw_a_completion_recorded_again_after_it_counted_the_straggler()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        var raced = false;
        var recordWrites = 0;
        host.Probe.BeforeCommand = async text =>
        {
            // The audit's first write to the record is its claim, taken before it counts; its second is the withdrawal.
            if (raced || !IsRecordWrite(text) || ++recordWrites < 2)
                return;
            raced = true;
            // Another worker, between the audit's count and its withdrawal and once the audit's claim has lapsed: it
            // rewrites the straggler, withdraws the completion over it, and a new verification pass records it again.
            clock.Advance(TimeSpan.FromMinutes(2));
            await using (var context = database.Context())
            {
                var row = await context.Orders.SingleAsync(order => order.Id == "straggler");
                BackfillStore.Write(row, BackfillStore.Read(host.Gate.Families.Chains.Single(), row), "2");
                await context.SaveChangesAsync();
            }

            await WithStoreAsync(async store =>
            {
                var withdrawn = await store.WithdrawCompletionAsync(BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, HostB, "host-b found a straggler", worker: null);
                await store.RecordCompletionAsync(BackfillFamily.Family, withdrawn.Record.Revision, "2", clock.GetUtcNow(), clock.GetUtcNow(), BackfillFamily.Chain, HostB, worker: null);
            });
        };

        await host.RunOnceAsync();

        Assert.True(raced);
        var record = await database.RecordAsync();
        Assert.Equal(("2", "host-b"), CompletedBy(record));
        Assert.Equal(SchemaFinishTransition.Completed, record.FinishHistory[^1].Transition);
        Assert.Single(record.FinishHistory, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
    }

    /// <summary>
    /// FR-018, the case a read taken when the run began would hide: another worker records the completion while this
    /// worker's run goes on, and then a writer still at 1 leaves a late row below it. This worker reads what stands before
    /// each row, so it withdraws that completion, naming the row's table, before it rewrites the row, rather than rewriting
    /// it silently under a completion that still stands; then it records the completion again.
    /// </summary>
    [Fact]
    public async Task A_completion_another_worker_records_mid_run_is_withdrawn_before_a_late_row_below_it_is_rewritten()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a", claim: TimeSpan.Zero);
        var other = await HostAsync("host-b", claim: TimeSpan.Zero);
        var interleaved = false;
        host.Probe.AfterWrite = async _ =>
        {
            if (interleaved)
                return;
            interleaved = true;
            // Between two of this worker's rows, the other runs a whole round: it upgrades what is left, verifies and records.
            await other.RunOnceAsync();
            Assert.Equal(("2", "host-b"), CompletedBy(await database.RecordAsync()));
            // After every order this worker has selected, so it meets the row later in the same run.
            await database.SeedAsync(Order("z-late", 5));
        };

        await host.RunOnceAsync();

        Assert.True(interleaved);
        var history = (await database.RecordAsync()).FinishHistory;
        var withdrawal = Assert.Single(history, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
        Assert.Equal(("2", "host-a"), (withdrawal.Version, withdrawal.Actor.Member!.HostId));
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1 (to be rewritten)", withdrawal.Reason);
        Assert.Equal(
            [(SchemaFinishTransition.Completed, "host-b"), (SchemaFinishTransition.Withdrawn, "host-a"), (SchemaFinishTransition.Completed, "host-a")],
            history.Skip(1).Select(entry => (entry.Transition, entry.Actor.Member!.HostId)));
        Assert.Contains("BackfillOrderRow:z-late", host.Probe.WrittenRows);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 2 ", row));
    }

    /// <summary>
    /// FR-018, the case logging and going on would hide: a withdrawal that loses its compare-and-set on every attempt stops
    /// the round rather than rewrite the straggler under a completion that still stands, so the next round finds it again.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_that_loses_every_compare_and_set_stops_the_round_and_rewrites_nothing()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        await database.SeedAsync(Order("straggler", 7));
        clock.Advance(AuditInterval);
        var rows = await database.SnapshotAsync();
        var written = host.Probe.WrittenRows.Count;
        var lost = 0;
        host.Probe.BeforeCommand = async text =>
        {
            if (!IsRecordWrite(text))
                return;
            lost++;
            // Another writer changes the record between each attempt's read and its write.
            await using var context = database.Context();
            await context.Set<EfSchemaFinalizationRecordRow>().ExecuteUpdateAsync(row => row.SetProperty(record => record.Revision, record => record.Revision + 1));
        };

        var exhausted = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunOnceAsync());

        Assert.Contains("attempts to withdraw the completion", exhausted.Message);
        // Three lost claims, after which the audit goes on unclaimed, since nothing correct depends on the claim (FR-008);
        // then three lost withdrawals, which stop it.
        Assert.Equal(6, lost);
        Assert.Equal(rows, await database.SnapshotAsync());
        Assert.Equal(written, host.Probe.WrittenRows.Count);
        var record = await database.RecordAsync();
        Assert.Equal("2", record.Finish!.CompletionVersion);
        Assert.DoesNotContain(record.FinishHistory, entry => entry.Transition == SchemaFinishTransition.Withdrawn);
    }

    /// <summary>
    /// FR-023: a family blocked by rows nothing may upgrade is surveyed again at the audit interval, not every round, so
    /// a blocker that persists costs one selection by stamp per interval; a new target is surveyed at once.
    /// </summary>
    [Fact]
    public async Task A_blocked_family_is_surveyed_again_at_the_audit_interval_not_every_round()
    {
        await SeedFamilyAsync();
        await database.SeedAsync(Receipt("hash-1"));
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillState.Blocked, host.Status.State);
        var selections = CountSelections(host);

        await host.RunOnceAsync();
        clock.Advance(AuditInterval - TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal(0, selections());
        Assert.Equal(EfSchemaBackfillState.Blocked, host.Status.State);
        Assert.Equal(EfSchemaBackfillBlockerKind.ContentAddressed, Assert.Single(host.Status.Blockers).Kind);

        clock.Advance(TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.True(selections() > 0);
        Assert.Equal(EfSchemaBackfillState.Blocked, host.Status.State);
    }

    /// <summary>
    /// FR-023 with FR-006, both ways: a family blocked by a row an operator repairs in place is surveyed again at the
    /// shorter repairable interval, not the audit interval, so a repair is seen within minutes; before that interval it is
    /// left alone.
    /// </summary>
    [Fact]
    public async Task A_family_blocked_by_a_row_an_operator_repairs_is_surveyed_again_at_the_repairable_interval()
    {
        await SeedFamilyAsync();
        var corrupt = Order("corrupt", 1);
        corrupt.ContentJson = "{ not json";
        await database.SeedAsync(corrupt);
        var host = await HostAsync("host-a");
        await host.RunOnceAsync();
        Assert.Equal(EfSchemaBackfillBlockerKind.Corruption, Assert.Single(host.Status.Blockers).Kind);
        await using (var context = host.Context())
            await context.Orders.Where(row => row.Id == "corrupt").ExecuteDeleteAsync();
        var selections = CountSelections(host);

        clock.Advance(RepairableInterval - TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal(0, selections());
        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);

        clock.Advance(TimeSpan.FromSeconds(1));
        await host.RunOnceAsync();

        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
    }

    /// <summary>
    /// A scope runner that returns without running the backfill's work fails the round, rather than being read as "nothing
    /// found", which a verification pass would record a completion on.
    /// </summary>
    [Fact]
    public async Task A_scope_runner_that_skips_the_work_fails_the_round_and_records_nothing()
    {
        await SeedFamilyAsync();
        var host = await HostAsync("host-a");
        var before = await database.RecordAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Backfill.RunOnceAsync((_, _) => Task.CompletedTask));

        Assert.Equal(before.Revision, (await database.RecordAsync()).Revision);
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 1 ", row));
    }

    /// <summary>
    /// A host that has recorded the family complete at 2, then, for <paramref name="current"/> "3", a host that writes 3 in
    /// its place, whose first round's audit of 2 finds nothing.
    /// </summary>
    private async Task<BackfillHost> CompleteAtTwoThenAsync(string current)
    {
        var host = await HostAsync("host-previous");
        await host.RunOnceAsync();
        Assert.Equal("2", (await database.RecordAsync()).Finish!.CompletionVersion);
        if (current == "2")
            return host;
        members["host-previous"].Live = false;
        return await HostAsync("host-a", current: current);
    }

    /// <summary>Counts, from now on, the commands <paramref name="host"/> sends that select from the family's tables.</summary>
    private static Func<int> CountSelections(BackfillHost host)
    {
        var selections = 0;
        host.Probe.BeforeCommand = text =>
        {
            if (new[] { BackfillFamily.OrdersTable, BackfillFamily.LinesTable, BackfillFamily.ReceiptsTable }.Any(table => text.Contains(table, StringComparison.Ordinal)))
                selections++;
            return Task.CompletedTask;
        };
        return () => selections;
    }

    private static readonly string[] FamilyTables = [BackfillFamily.OrdersTable, BackfillFamily.LinesTable, BackfillFamily.ReceiptsTable];

    /// <summary>Takes the family's claim as another worker would once this host's claim has expired: for <paramref name="target"/>, for a minute.</summary>
    private Task TakeClaimAsync(string target) =>
        WithStoreAsync(async store =>
            Assert.True((await store.ClaimBackfillAsync(
                BackfillFamily.Family, (await store.FindAsync(BackfillFamily.Family))!.Revision, target, BackfillFamily.Chain, HostB, "worker-b", TimeSpan.FromMinutes(1))).Applied));

    /// <summary>A second family of the module, at versions 1 and 2, that holds no table: its run has nothing to rewrite and records completion.</summary>
    private static readonly EfSchemaFamilyDescriptor TablelessFamily = new("BackfillNotes", BackfillFamily.Module, "2", typeof(BackfillFamily).Assembly)
    {
        Upcasters = [new EfSchemaUpcasterDescriptor(typeof(BackfillFamily.AddNote), "1", "2")]
    };

    private async Task<SchemaFinalizationRecord> RecordOfAsync(string family)
    {
        await using var context = database.Context();
        return (await new EfSchemaFinalizationStore(context, clock).FindAsync(family))!;
    }

    /// <summary>Counts, from now on, the writes <paramref name="host"/> makes to the family's finalization record.</summary>
    private static Func<int> CountRecordWrites(BackfillHost host)
    {
        var writes = 0;
        host.Probe.BeforeCommand = text =>
        {
            if (IsRecordWrite(text))
                writes++;
            return Task.CompletedTask;
        };
        return () => writes;
    }

    /// <summary>Counts, from now on, every command <paramref name="host"/> sends, to the family's tables or to its record.</summary>
    private static Func<int> CountCommands(BackfillHost host)
    {
        var commands = 0;
        host.Probe.BeforeCommand = _ =>
        {
            commands++;
            return Task.CompletedTask;
        };
        return () => commands;
    }

    /// <summary>
    /// The proof <paramref name="after"/> carries is the one <paramref name="before"/> did: the finalized version, the
    /// completion and both histories, whatever claim each was taken under (FR-008).
    /// </summary>
    private static void AssertProofUnchanged(SchemaFinalizationRecord before, SchemaFinalizationRecord after)
    {
        Assert.Equal(before.FinalizedVersion, after.FinalizedVersion);
        Assert.Equal(before.Finish! with { Run = null }, after.Finish! with { Run = null });
        Assert.Equal(before.History, after.History);
        Assert.Equal(before.FinishHistory, after.FinishHistory);
    }

    private static bool IsRecordWrite(string text) =>
        text.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && text.Contains(RecordTable, StringComparison.Ordinal);

    private static (string Version, string HostId) CompletedBy(SchemaFinalizationRecord record) =>
        (record.Finish!.CompletionVersion, record.Finish.RecordedBy.Member!.HostId);

    private async Task WithStoreAsync(Func<EfSchemaFinalizationStore, Task> action)
    {
        await using var context = database.Context();
        await action(new EfSchemaFinalizationStore(context, clock));
    }

    private async Task<IReadOnlyList<string>> UninterruptedSnapshotAsync()
    {
        await using var reference = new TemporarySqliteDatabase("schema-backfill-reference");
        var referenceDatabase = new BackfillDatabase(builder => builder.UseSqlite(reference.ConnectionString));
        await referenceDatabase.CreateAsync(clock);
        await SeedFamilyAsync(referenceDatabase);
        var member = new FakeMember("reference").Reading(BackfillFamily.Family, "1", "2");
        await using var host = new BackfillHost(referenceDatabase, new FakeFleet(new FakeFleetState(), member), Options(), clock, observations: member.Observations = new());
        await host.ActivateAsync();
        await host.RunOnceAsync();
        return await referenceDatabase.SnapshotAsync();
    }

    private Task SeedFamilyAsync() => SeedFamilyAsync(database);

    private static Task SeedFamilyAsync(BackfillDatabase target) =>
        target.SeedAsync(
            Order("o1", 10), Order("o2", 20), Order("o3", 30), Order("o4", 40), Order("o5", 50),
            Line("o1", 1), Line("o1", 2), Line("o2", 1));

    private async Task<BackfillHost> HostAsync(
        string hostId,
        EfSchemaModuleFamilies? families = null,
        TimeSpan? margin = null,
        TimeSpan? claim = null,
        int verificationPasses = 3,
        string current = "2",
        TimeSpan? configuredMargin = null,
        ILogger? logger = null)
    {
        families ??= BackfillFamily.Families(current);
        var member = members[hostId] = fleet.Add(new FakeMember(hostId));
        foreach (var chain in families.Chains)
            member.Reading(chain.Family, [.. chain.ReadableVersions]);
        member.Observations = new EfSchemaFinalizationObservations();
        var host = new BackfillHost(
            database,
            new FakeFleet(fleet, member) { SettleMargin = margin ?? TimeSpan.Zero },
            Options(claim, verificationPasses, configuredMargin),
            clock,
            families,
            member.Observations,
            logger);
        hosts.Add(host);
        await host.ActivateAsync();
        return host;
    }

    private static EfSchemaBackfillOptions Options(TimeSpan? claim = null, int verificationPasses = 3, TimeSpan? settleMargin = null) => new()
    {
        SettleMargin = settleMargin,
        BatchSize = 2,
        BatchPause = TimeSpan.Zero,
        ClaimDuration = claim ?? TimeSpan.FromMinutes(1),
        AuditInterval = AuditInterval,
        RepairableBlockerInterval = RepairableInterval,
        VerificationPasses = verificationPasses
    };

    /// <summary>What each order and line reads as through the family's read path: SC-001's "reads equal to its value before".</summary>
    private static async Task<IReadOnlyList<object>> DomainValuesAsync(BackfillHost host)
    {
        await using var context = host.Context();
        var chain = host.Gate.Families.Chains.Single();
        var orders = (await context.Orders.AsNoTracking().OrderBy(row => row.Id).ToListAsync()).Select(row => (object)BackfillStore.Read(chain, row));
        var lines = (await context.Lines.AsNoTracking().OrderBy(row => row.OrderId).ThenBy(row => row.Number).ToListAsync()).Select(row => (object)BackfillStore.Read(chain, row));
        return [.. orders, .. lines];
    }

    private static IReadOnlyList<string> Receipts(IEnumerable<string> snapshot) =>
        snapshot.Where(row => row.StartsWith("receipt", StringComparison.Ordinal)).ToArray();

    private static readonly SchemaFinalizationMember HostB = new("host-b", "b");

    private sealed class HostKilledException : Exception;

    /// <summary>A rewriter that takes a service no shell of these tests composes.</summary>
    private sealed class RewriterWithAnUncomposedService(IServiceScopeFactory unused, UncomposedService service) : IEfSchemaRowRewriter
    {
        public ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"{unused.GetType().Name}{service.GetType().Name} cannot have been constructed.");
    }

    private sealed class UncomposedService;
}
