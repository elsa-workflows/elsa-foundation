using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeFleetState fleet = new();
    private readonly List<BackfillHost> hosts = [];
    private readonly Dictionary<string, FakeMember> members = new(StringComparer.Ordinal);
    private readonly BackfillDatabase database;

    public EfSchemaBackfillTests() => database = new BackfillDatabase(builder => builder.UseSqlite(file.ConnectionString));

    public Task InitializeAsync() => database.CreateAsync(clock);

    public async Task DisposeAsync()
    {
        foreach (var host in hosts)
            await host.DisposeAsync();
        await file.DisposeAsync();
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

    /// <summary>FR-007, both ways: a row at the target is skipped, and a rerun, on this host or another, writes nothing.</summary>
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
        var other = await HostAsync("host-b");
        await other.RunOnceAsync();

        Assert.Equal(2, host.Probe.WrittenRows.Count);
        Assert.Empty(other.Probe.WrittenRows);
        Assert.Equal(after, await database.SnapshotAsync());
        Assert.Equal(record.Revision, (await database.RecordAsync()).Revision);
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
    /// nothing; a straggler written after completion by a writer still at 1 is rewritten, reported, and withdraws the
    /// completion but not the finalized version, until a new verification pass records it again.
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
        Assert.Equal(complete.Revision, (await database.RecordAsync()).Revision);
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

        // A receipt a version-2 writer produced sits beside them: once the old ones are gone, the family completes.
        await using (var context = host.Context())
            await context.Receipts.Where(row => row.SchemaVersion == "1").ExecuteDeleteAsync();
        await database.SeedAsync(Receipt("hash-3", "2"));
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

        Assert.Equal("1", (await database.RecordAsync()).Finish!.CompletionVersion);
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
        // Lines are upgraded first, then orders, two rows a batch: o1/1 and o1/2, then o2/1; then o1 and o2, o3 and o4, o5.
        first.Probe.BeforeWrite = _ => crash switch
        {
            // o2/1: the first row of the second batch, before any of it is written.
            "between batches" when writes == 2 => throw new HostKilledException(),
            // o2: the second row of a batch whose first row, o1, is written.
            "before a write" when writes == 4 => throw new HostKilledException(),
            _ => Task.CompletedTask
        };
        // o1 is written, and the host dies before o2 of the same batch is read.
        first.Probe.AfterWrite = _ => ++writes == 4 && crash == "after a write" ? throw new HostKilledException() : Task.CompletedTask;

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
        var bothRead = new CountdownEvent(2);
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
    /// FR-018, both ways within one run: a run towards a newer finalized version that meets a row below the completion
    /// that stands withdraws it, naming the table and the count, before it records the newer one; the rows between the
    /// two versions are not stragglers and withdraw nothing.
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
        Assert.Contains($"'{BackfillFamily.OrdersTable}': 1 (rewritten)", history[^2].Reason);
        Assert.Equal((SchemaFinishTransition.Completed, "3"), (history[^1].Transition, history[^1].Version));
        Assert.All(await database.SnapshotAsync(), row => Assert.Contains(" 3 ", row));
    }

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
        string current = "2")
    {
        families ??= BackfillFamily.Families(current);
        var member = members[hostId] = fleet.Add(new FakeMember(hostId).Reading(BackfillFamily.Family, [.. families.Chains.Single().ReadableVersions]));
        member.Observations = new EfSchemaFinalizationObservations();
        var host = new BackfillHost(
            database,
            new FakeFleet(fleet, member) { SettleMargin = margin ?? TimeSpan.Zero },
            Options(claim, verificationPasses),
            clock,
            families,
            member.Observations);
        hosts.Add(host);
        await host.ActivateAsync();
        return host;
    }

    private static EfSchemaBackfillOptions Options(TimeSpan? claim = null, int verificationPasses = 3) => new()
    {
        BatchSize = 2,
        BatchPause = TimeSpan.Zero,
        ClaimDuration = claim ?? TimeSpan.FromMinutes(1),
        AuditInterval = TimeSpan.FromHours(1),
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

    private sealed class HostKilledException : Exception;

    /// <summary>A rewriter that takes a service no shell of these tests composes.</summary>
    private sealed class RewriterWithAnUncomposedService(IServiceScopeFactory unused, UncomposedService service) : IEfSchemaRowRewriter
    {
        public ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"{unused}{service} cannot have been constructed.");
    }

    private sealed class UncomposedService;
}
