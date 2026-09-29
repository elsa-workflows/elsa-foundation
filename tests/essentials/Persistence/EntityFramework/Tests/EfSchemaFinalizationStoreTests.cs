using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The finalization record's own rules (spec 181, FR-001 to FR-004 and FR-019; spec 186, FR-014 to FR-016 and
/// FR-018), on a file database so two stores can race through two connections. Every refusal is also checked to have
/// written nothing, because a refused change that half-landed would look like a refusal and still move the record.
/// </summary>
public sealed class EfSchemaFinalizationStoreTests : IAsyncLifetime
{
    private const string Family = "Probe";
    private static readonly string[] Chain = ["1", "2", "3", "4"];
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly SchemaFinalizationMember HostA = new("host-a", "incarnation-a");
    private static readonly SchemaFinalizationMember HostB = new("host-b", "incarnation-b");
    private static readonly SchemaFinalizationActor Operator = SchemaFinalizationActor.OfOperator("ops@example");

    private readonly TemporarySqliteDatabase database = new("schema-finalization");
    private readonly List<DbContext> contexts = [];

    public async Task InitializeAsync() => await Context().Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task A_new_record_is_finalized_and_complete_at_its_initial_version_and_names_its_creator()
    {
        var record = await CreatedAsync();

        Assert.Equal(Family, record.Family);
        Assert.Equal(1, record.Revision);
        Assert.Equal("1", record.FinalizedVersion);
        Assert.Null(record.Intent);
        Assert.Empty(record.Holds);
        Assert.Equal([new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.Created, "1", Operator, Now)], record.History);
        Assert.Equal(new SchemaFinishRecord("1", null, null, Operator), record.Finish);
        Assert.Equal([new SchemaFinishHistoryEntry(SchemaFinishTransition.Completed, "1", Operator, Now)], record.FinishHistory);
        Assert.Equal(record, await FoundAsync(), RecordComparer);
    }

    /// <summary>Creation is the one path that writes a finalized version without an intent, so it must never overwrite one.</summary>
    [Fact]
    public async Task An_existing_record_is_returned_as_it_stands_and_never_recreated_at_another_version()
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "3");
        var raw = await RawAsync();

        Assert.Equal("3", (await Store().GetOrCreateAsync(Family, "1", Chain, Operator)).FinalizedVersion);
        Assert.Equal("3", (await Store().GetOrCreateAsync(Family, "4", Chain, Operator)).FinalizedVersion);
        Assert.Equal(finalized, await FoundAsync(), RecordComparer);
        Assert.Equal(raw, await RawAsync());
    }

    [Fact]
    public async Task A_version_moves_from_pending_to_readable_everywhere_to_finalized_and_every_step_is_in_the_history()
    {
        var created = await CreatedAsync();
        Assert.Equal(SchemaFinalizationState.Finalized, created.StateOf("1", Chain));
        Assert.Equal(SchemaFinalizationState.Pending, created.StateOf("3", Chain));

        var intended = Applied(await Store().RecordIntentAsync(Family, created.Revision, "3", Chain, HostA));
        Assert.Equal(new SchemaFinalizationIntent("3", HostA, Now), intended.Intent);
        Assert.Equal(SchemaFinalizationState.ReadableEverywhere, intended.StateOf("2", Chain));
        Assert.Equal(SchemaFinalizationState.ReadableEverywhere, intended.StateOf("3", Chain));
        Assert.Equal(SchemaFinalizationState.Pending, intended.StateOf("4", Chain));

        var finalized = Applied(await Store().CommitIntentAsync(Family, intended.Revision, Chain, HostB));
        Assert.Equal("3", finalized.FinalizedVersion);
        Assert.Null(finalized.Intent);
        Assert.Equal(SchemaFinalizationState.Finalized, finalized.StateOf("3", Chain));
        Assert.Equal(3, finalized.Revision);
        Assert.Equal(
            [
                new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.Created, "1", Operator, Now),
                new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.IntentRecorded, "3", SchemaFinalizationActor.Of(HostA), Now),
                new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.Finalized, "3", SchemaFinalizationActor.Of(HostB), Now)
            ],
            finalized.History);
        Assert.Equal(finalized, await FoundAsync(), RecordComparer);
    }

    /// <summary>
    /// Spec 181, FR-003: the finalized version only moves forward. Recording an intent is the only way towards a new
    /// finalized version, so a store that let a backwards or repeated intent through would finalize it on commit.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public async Task A_backwards_or_repeated_finalization_is_refused_and_writes_nothing(string version)
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "3");
        var raw = await RawAsync();

        await AssertRefusedAsync(SchemaFinalizationRefusal.NotForward,
            () => Store().RecordIntentAsync(Family, finalized.Revision, version, Chain, HostA));

        Assert.Equal(raw, await RawAsync());
        Assert.Equal("3", (await FoundAsync()).FinalizedVersion);
    }

    [Fact]
    public async Task An_abandoned_intent_returns_its_version_to_pending_and_the_finalized_version_stays()
    {
        var intended = Applied(await Store().RecordIntentAsync(Family, (await CreatedAsync()).Revision, "2", Chain, HostA));

        var abandoned = Applied(await Store().AbandonIntentAsync(Family, intended.Revision, HostB, "host-c reads only 1"));

        Assert.Null(abandoned.Intent);
        Assert.Equal("1", abandoned.FinalizedVersion);
        Assert.Equal(SchemaFinalizationState.Pending, abandoned.StateOf("2", Chain));
        Assert.Equal(
            new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.IntentAbandoned, "2", SchemaFinalizationActor.Of(HostB), Now, "host-c reads only 1"),
            abandoned.History[^1]);
    }

    [Fact]
    public async Task Commit_and_abandon_need_an_intent_and_a_second_intent_waits_for_the_first()
    {
        var created = await CreatedAsync();
        await AssertRefusedAsync(SchemaFinalizationRefusal.NoIntent, () => Store().CommitIntentAsync(Family, created.Revision, Chain, HostA));
        await AssertRefusedAsync(SchemaFinalizationRefusal.NoIntent, () => Store().AbandonIntentAsync(Family, created.Revision, HostA));

        var intended = Applied(await Store().RecordIntentAsync(Family, created.Revision, "2", Chain, HostA));
        var raw = await RawAsync();

        await AssertRefusedAsync(SchemaFinalizationRefusal.IntentInFlight, () => Store().RecordIntentAsync(Family, intended.Revision, "3", Chain, HostB));
        Assert.Equal(raw, await RawAsync());
    }

    /// <summary>
    /// A writer can only order versions its own chain names. One that cannot place the finalized version is exactly a
    /// host that cannot read it (spec 181, FR-012), and it must not move the record.
    /// </summary>
    [Fact]
    public async Task A_version_outside_the_writers_chain_is_refused()
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "4");
        var raw = await RawAsync();
        string[] olderChain = ["1", "2", "3"];

        await AssertRefusedAsync(SchemaFinalizationRefusal.UnknownVersion, () => Store().RecordIntentAsync(Family, finalized.Revision, "5", Chain, HostA));
        await AssertRefusedAsync(SchemaFinalizationRefusal.UnknownVersion, () => Store().RecordIntentAsync(Family, finalized.Revision, "3", olderChain, HostA));
        await AssertRefusedAsync(SchemaFinalizationRefusal.UnknownVersion, () => Store().PlaceHoldAsync(Family, finalized.Revision, "3", "canary", "ops", olderChain));
        Assert.Throws<SchemaFinalizationRefusedException>(() => finalized.StateOf("3", olderChain));
        Assert.Equal(raw, await RawAsync());
    }

    /// <summary>A hold only ever delays finalization, so a family-wide one needs no position and is never refused for want of one.</summary>
    [Fact]
    public async Task A_family_wide_hold_is_placed_even_by_a_writer_whose_chain_cannot_place_the_finalized_version()
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "4");

        var held = Applied(await Store().PlaceHoldAsync(Family, finalized.Revision, null, "canary", "ops", ["1", "2", "3"]));

        Assert.Equal([new SchemaFinalizationHold(null, "canary", "ops", Now)], held.Holds);
    }

    [Fact]
    public async Task A_write_that_names_a_stale_revision_is_a_lost_compare_and_set_and_writes_nothing()
    {
        var created = await CreatedAsync();
        var intended = Applied(await Store().RecordIntentAsync(Family, created.Revision, "2", Chain, HostA));
        var raw = await RawAsync();

        var stale = await Store().AbandonIntentAsync(Family, created.Revision, HostB);

        Assert.False(stale.Applied);
        Assert.Equal(intended, stale.Record, RecordComparer);
        Assert.Equal(raw, await RawAsync());
    }

    [Fact]
    public async Task Of_two_concurrent_writers_exactly_one_wins()
    {
        var created = await CreatedAsync();
        var (first, second) = (Store(), Store());

        var writes = await Task.WhenAll(
            first.RecordIntentAsync(Family, created.Revision, "2", Chain, HostA),
            second.RecordIntentAsync(Family, created.Revision, "3", Chain, HostB));

        var winner = Assert.Single(writes, write => write.Applied).Record;
        var loser = Assert.Single(writes, write => !write.Applied).Record;
        var stored = await FoundAsync();
        Assert.Equal(winner, stored, RecordComparer);
        Assert.Equal(winner, loser, RecordComparer);
        Assert.Equal(created.Revision + 1, stored.Revision);
        Assert.Single(stored.History, entry => entry.Transition == SchemaFinalizationTransition.IntentRecorded);
    }

    /// <summary>
    /// The case the revision compare exists for. An evaluator read the record with its intent, confirmed, and commits;
    /// between its read and its save another member abandons that intent. The commit must lose at the database, not
    /// finalize a version whose intent was already abandoned.
    /// </summary>
    [Fact]
    public async Task A_writer_whose_record_changes_between_its_read_and_its_save_loses_at_the_database()
    {
        var intended = Applied(await Store().RecordIntentAsync(Family, (await CreatedAsync()).Revision, "2", Chain, HostA));
        var abandonInBetween = new BeforeFirstSave(() => Store().AbandonIntentAsync(Family, intended.Revision, HostB, "a joiner reads only 1"));

        var commit = await Store(abandonInBetween).CommitIntentAsync(Family, intended.Revision, Chain, HostA);

        Assert.True(abandonInBetween.Ran);
        Assert.False(commit.Applied);
        var stored = await FoundAsync();
        Assert.Equal(stored, commit.Record, RecordComparer);
        Assert.Equal("1", stored.FinalizedVersion);
        Assert.Null(stored.Intent);
        Assert.DoesNotContain(stored.History, entry => entry.Transition == SchemaFinalizationTransition.Finalized);
        Assert.Equal(SchemaFinalizationTransition.IntentAbandoned, stored.History[^1].Transition);
    }

    [Fact]
    public async Task A_family_wide_hold_keeps_every_version_pending_until_released()
    {
        var held = Applied(await Store().PlaceHoldAsync(Family, (await CreatedAsync()).Revision, null, "canary", "ops", Chain));
        Assert.Equal([new SchemaFinalizationHold(null, "canary", "ops", Now)], held.Holds);
        Assert.All(Chain[1..], version => Assert.Equal(SchemaFinalizationState.Pending, held.StateOf(version, Chain)));

        await AssertRefusedAsync(SchemaFinalizationRefusal.Held, () => Store().RecordIntentAsync(Family, held.Revision, "2", Chain, HostA));

        var released = Applied(await Store().ReleaseHoldAsync(Family, held.Revision, null, "ops"));
        Assert.Empty(released.Holds);
        Assert.Equal(
            [SchemaFinalizationTransition.HoldPlaced, SchemaFinalizationTransition.HoldReleased],
            released.History.Skip(1).Select(entry => entry.Transition));
        Assert.True(Applied(await Store().RecordIntentAsync(Family, released.Revision, "2", Chain, HostA)).Intent is not null);
    }

    /// <summary>Finalizing a later version would move the rollback boundary past the held one, so it is held too.</summary>
    [Fact]
    public async Task A_hold_on_a_version_also_holds_every_later_one_but_not_an_earlier_one()
    {
        var held = Applied(await Store().PlaceHoldAsync(Family, (await CreatedAsync()).Revision, "3", "canary", "ops", Chain));

        Assert.Equal(SchemaFinalizationState.Pending, held.StateOf("3", Chain));
        Assert.True(held.IsHeld("4", Chain));
        Assert.False(held.IsHeld("2", Chain));
        await AssertRefusedAsync(SchemaFinalizationRefusal.Held, () => Store().RecordIntentAsync(Family, held.Revision, "3", Chain, HostA));
        await AssertRefusedAsync(SchemaFinalizationRefusal.Held, () => Store().RecordIntentAsync(Family, held.Revision, "4", Chain, HostA));

        Assert.Equal("2", (await FinalizedAsync(held, "2")).FinalizedVersion);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    public async Task Holding_a_finalized_version_is_refused_because_the_rollback_boundary_has_been_crossed(string version)
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "3");
        var raw = await RawAsync();

        var refusal = await AssertRefusedAsync(SchemaFinalizationRefusal.RollbackBoundaryCrossed,
            () => Store().PlaceHoldAsync(Family, finalized.Revision, version, "too late", "ops", Chain));

        Assert.Contains("rollback boundary has been crossed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(raw, await RawAsync());
    }

    [Fact]
    public async Task A_duplicate_hold_and_a_release_of_no_hold_are_refused()
    {
        var held = Applied(await Store().PlaceHoldAsync(Family, (await CreatedAsync()).Revision, "3", "canary", "ops", Chain));
        var raw = await RawAsync();

        await AssertRefusedAsync(SchemaFinalizationRefusal.HoldAlreadyPlaced, () => Store().PlaceHoldAsync(Family, held.Revision, "3", "again", "ops", Chain));
        await AssertRefusedAsync(SchemaFinalizationRefusal.NoHold, () => Store().ReleaseHoldAsync(Family, held.Revision, null, "ops"));
        await AssertRefusedAsync(SchemaFinalizationRefusal.NoHold, () => Store().ReleaseHoldAsync(Family, held.Revision, "4", "ops"));
        Assert.Equal(raw, await RawAsync());
    }

    /// <summary>
    /// Spec 181, User Story 4, scenario 4: a hold placed while an intent is being confirmed abandons it. An evaluator
    /// whose confirming read began before the hold then loses its commit, so the hold cannot be overtaken by a
    /// finalization that looks legitimate. A hold that does not cover the intent leaves it to commit.
    /// </summary>
    [Fact]
    public async Task A_hold_abandons_the_intent_it_covers_so_a_confirmation_that_began_before_it_cannot_finalize()
    {
        var intended = Applied(await Store().RecordIntentAsync(Family, (await CreatedAsync()).Revision, "3", Chain, HostA));

        var held = Applied(await Store().PlaceHoldAsync(Family, intended.Revision, "2", "canary", "ops", Chain));
        Assert.Null(held.Intent);
        Assert.Equal(
            [SchemaFinalizationTransition.IntentAbandoned, SchemaFinalizationTransition.HoldPlaced],
            held.History.TakeLast(2).Select(entry => entry.Transition));

        var commit = await Store().CommitIntentAsync(Family, intended.Revision, Chain, HostA);
        Assert.False(commit.Applied);
        Assert.Equal("1", (await FoundAsync()).FinalizedVersion);
    }

    [Fact]
    public async Task A_hold_on_a_later_version_leaves_an_intent_it_does_not_cover_to_commit()
    {
        var intended = Applied(await Store().RecordIntentAsync(Family, (await CreatedAsync()).Revision, "2", Chain, HostA));

        var held = Applied(await Store().PlaceHoldAsync(Family, intended.Revision, "3", "canary", "ops", Chain));
        Assert.Equal(intended.Intent, held.Intent);

        Assert.Equal("2", Applied(await Store().CommitIntentAsync(Family, held.Revision, Chain, HostA)).FinalizedVersion);
    }

    [Fact]
    public async Task The_database_identity_is_created_once_and_stays_the_same_across_reads_restarts_and_families()
    {
        Assert.Null(await Store().FindDatabaseIdentityAsync());
        var identity = await Store().GetOrCreateDatabaseIdentityAsync();
        Assert.Matches("^[0-9a-f]{32}$", identity);

        var first = await CreatedAsync();
        var second = await Store().GetOrCreateAsync("Another", "2", Chain, Operator);
        await DisposeContextsAsync();
        SqliteConnection.ClearAllPools();

        Assert.Equal(identity, await Store().GetOrCreateDatabaseIdentityAsync());
        Assert.Equal(identity, await Store().FindDatabaseIdentityAsync());
        Assert.Equal(identity, first.DatabaseIdentity);
        Assert.Equal(identity, second.DatabaseIdentity);
        Assert.Equal(identity, (await FoundAsync()).DatabaseIdentity);
        Assert.Equal(1, await Context().Set<EfDatabaseIdentityRow>().CountAsync());
    }

    /// <summary>The race the identity table's one-row key exists for: two families first written at once on an empty database.</summary>
    [Fact]
    public async Task Two_creators_racing_on_an_empty_database_agree_on_one_identity_and_one_record_per_family()
    {
        var records = await Task.WhenAll(
            Store().GetOrCreateAsync(Family, "1", Chain, Operator),
            Store().GetOrCreateAsync(Family, "1", Chain, SchemaFinalizationActor.OfOperator("someone-else")),
            Store().GetOrCreateAsync("Another", "1", Chain, Operator),
            Store().GetOrCreateAsync("Third", "1", Chain, Operator));

        var identity = Assert.Single(records.Select(record => record.DatabaseIdentity).Distinct());
        Assert.Equal(identity, await Store().FindDatabaseIdentityAsync());
        Assert.Equal(records[0], records[1], RecordComparer);
        Assert.Equal(3, await Context().Set<EfSchemaFinalizationRecordRow>().CountAsync());
    }

    /// <summary>
    /// Spec 181, Decisions, Q11: never derived from a connection string. The same connection string in front of a fresh
    /// database is a different database, and gets a different identity.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_behind_the_same_connection_string_gets_a_new_identity()
    {
        var before = await Store().GetOrCreateDatabaseIdentityAsync();
        await DisposeContextsAsync();
        TemporarySqliteDatabase.ClearPoolAndDeleteFiles(database.Path);
        await Context().Database.EnsureCreatedAsync();

        Assert.Null(await Store().FindDatabaseIdentityAsync());
        Assert.NotEqual(before, await Store().GetOrCreateDatabaseIdentityAsync());
    }

    [Fact]
    public async Task A_completion_only_moves_forward_and_never_past_the_finalized_version()
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "3");
        var raw = await RawAsync();
        await AssertRefusedAsync(SchemaFinalizationRefusal.CompletionBeyondFinalized,
            () => Store().RecordCompletionAsync(Family, finalized.Revision, "4", Now, Now, Chain, HostA));
        await AssertRefusedAsync(SchemaFinalizationRefusal.NotForward,
            () => Store().RecordCompletionAsync(Family, finalized.Revision, "1", Now, Now, Chain, HostA));
        Assert.Equal(raw, await RawAsync());

        var completed = Applied(await Store().RecordCompletionAsync(Family, finalized.Revision, "3", Now.AddMinutes(-5), Now.AddMinutes(-1), Chain, HostA));

        Assert.Equal(new SchemaFinishRecord("3", Now.AddMinutes(-5), Now.AddMinutes(-1), SchemaFinalizationActor.Of(HostA)), completed.Finish);
        Assert.Equal(new SchemaFinishHistoryEntry(SchemaFinishTransition.Completed, "3", SchemaFinalizationActor.Of(HostA), Now), completed.FinishHistory[^1]);
        await AssertRefusedAsync(SchemaFinalizationRefusal.NotForward,
            () => Store().RecordCompletionAsync(Family, completed.Revision, "2", Now, Now, Chain, HostB));
    }

    /// <summary>Spec 186, FR-018 and FR-019: withdrawing completion is not unfinalizing, and the finish history keeps both.</summary>
    [Fact]
    public async Task Withdrawing_a_completion_leaves_the_finalized_version_and_appends_to_the_finish_history()
    {
        var finalized = await FinalizedAsync(await CreatedAsync(), "2");
        var completed = Applied(await Store().RecordCompletionAsync(Family, finalized.Revision, "2", Now, Now, Chain, HostA));

        var withdrawn = Applied(await Store().WithdrawCompletionAsync(Family, completed.Revision, HostB, "3 rows below 2"));

        Assert.Null(withdrawn.Finish);
        Assert.Equal("2", withdrawn.FinalizedVersion);
        Assert.Equal(
            [SchemaFinishTransition.Completed, SchemaFinishTransition.Completed, SchemaFinishTransition.Withdrawn],
            withdrawn.FinishHistory.Select(entry => entry.Transition));
        await AssertRefusedAsync(SchemaFinalizationRefusal.NoCompletion, () => Store().WithdrawCompletionAsync(Family, withdrawn.Revision, HostB));
        Assert.Equal("2", Applied(await Store().RecordCompletionAsync(Family, withdrawn.Revision, "2", Now, Now, Chain, HostA)).Finish!.CompletionVersion);
    }

    /// <summary>
    /// A row a newer build wrote reports skew on read and on write, before its JSON is parsed: the corrupt history would
    /// otherwise surface as corruption. The next test is the witness that the same JSON at the current version does.
    /// </summary>
    [Fact]
    public async Task A_record_row_a_newer_build_wrote_reports_skew_before_its_json_is_read()
    {
        var created = await CreatedAsync();
        await EfSchemaVersionSkewTestSupport.ArrangeSkewedTableAsync(Context(), typeof(EfSchemaFinalizationRecordRow), nameof(EfSchemaFinalizationRecordRow.HistoryJson));
        var raw = await RawAsync();

        AssertSkew(await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().FindAsync(Family)));
        AssertSkew(await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().GetOrCreateAsync(Family, "1", Chain, Operator)));
        AssertSkew(await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().RecordIntentAsync(Family, created.Revision, "2", Chain, HostA)));
        Assert.Equal(raw, await RawAsync());
    }

    [Fact]
    public async Task A_current_record_row_whose_json_cannot_be_read_is_corruption_rather_than_skew()
    {
        await CreatedAsync();
        await EfSchemaVersionSkewTestSupport.ArrangeSkewedTableAsync(
            Context(), typeof(EfSchemaFinalizationRecordRow), nameof(EfSchemaFinalizationRecordRow.HistoryJson), EfSchemaFinalization.SchemaVersion);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => Store().FindAsync(Family));
        Assert.Contains(Family, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_identity_row_a_newer_build_wrote_reports_skew_and_is_never_replaced()
    {
        await Store().GetOrCreateDatabaseIdentityAsync();
        await EfSchemaVersionSkewTestSupport.ArrangeSkewedTableAsync(Context(), typeof(EfDatabaseIdentityRow), nameof(EfDatabaseIdentityRow.DatabaseIdentity));

        AssertSkew(await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().FindDatabaseIdentityAsync()));
        AssertSkew(await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().GetOrCreateDatabaseIdentityAsync()));
        Assert.Equal(1, await Context().Set<EfDatabaseIdentityRow>().CountAsync());
    }

    /// <summary>
    /// Pins the persisted JSON. The store serializes its public records directly, so renaming one of their members
    /// would silently change what is stored under the same stamp; this fails first.
    /// </summary>
    [Fact]
    public async Task The_persisted_json_of_a_record_is_pinned()
    {
        var created = await CreatedAsync();
        await Store().RecordIntentAsync(Family, created.Revision, "2", Chain, HostA);

        var row = await Context().Set<EfSchemaFinalizationRecordRow>().AsNoTracking().SingleAsync();

        Assert.Equal(EfSchemaFinalization.SchemaVersion, row.SchemaVersion);
        Assert.Equal("""{"version":"2","member":{"hostId":"host-a","incarnation":"incarnation-a"},"at":"2026-09-28T00:00:00+00:00"}""", row.IntentJson);
        Assert.Equal("[]", row.HoldsJson);
        Assert.Equal(
            """[{"transition":"Created","version":"1","actor":{"member":null,"operator":"ops@example"},"at":"2026-09-28T00:00:00+00:00","reason":null},""" +
            """{"transition":"IntentRecorded","version":"2","actor":{"member":{"hostId":"host-a","incarnation":"incarnation-a"},"operator":null},"at":"2026-09-28T00:00:00+00:00","reason":null}]""",
            row.HistoryJson);
        Assert.Equal(
            """{"completionVersion":"1","verificationStartedAt":null,"verificationEndedAt":null,"recordedBy":{"member":null,"operator":"ops@example"}}""",
            row.FinishJson);
        Assert.Equal(
            """[{"transition":"Completed","version":"1","actor":{"member":null,"operator":"ops@example"},"at":"2026-09-28T00:00:00+00:00","reason":null}]""",
            row.FinishHistoryJson);
    }

    [Fact]
    public async Task A_context_without_the_tables_or_with_unsaved_changes_of_its_own_is_refused()
    {
        await using var unmapped = new DbContext(new DbContextOptionsBuilder().UseSqlite(database.ConnectionString).Options);
        Assert.Throws<InvalidOperationException>(() => new EfSchemaFinalizationStore(unmapped));

        var created = await CreatedAsync();
        var context = Context();
        context.Add(new EfDatabaseIdentityRow { Id = 2, DatabaseIdentity = "pending", SchemaVersion = EfSchemaFinalization.SchemaVersion });
        var store = new EfSchemaFinalizationStore(context, new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordIntentAsync(Family, created.Revision, "2", Chain, HostA));
        Assert.Null((await FoundAsync()).Intent);
    }

    /// <summary>
    /// A module context that declares a schema family stamps and checks every row it saves and reads as that family.
    /// The finalization rows it maps belong to their own family, so they keep their own stamp and read back, while the
    /// module's own row beside them is still stamped as the module's.
    /// </summary>
    [Fact]
    public async Task A_context_that_declares_its_own_family_neither_stamps_nor_checks_the_finalization_rows_as_its_own()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        VersionedContext Versioned() => new(new DbContextOptionsBuilder<VersionedContext>().UseSqlite(connection).Options);
        await using (var setup = Versioned())
            await setup.Database.EnsureCreatedAsync();
        await using var context = Versioned();
        var store = new EfSchemaFinalizationStore(context, new FixedClock(Now));

        var created = await store.GetOrCreateAsync(Family, "1", Chain, Operator);
        Applied(await store.RecordIntentAsync(Family, created.Revision, "2", Chain, HostA));
        context.Add(new Widget { Id = "widget" });
        await context.SaveChangesAsync();

        await using var reader = Versioned();
        Assert.Equal(VersionedContext.Version, await reader.Set<Widget>().Select(widget => EF.Property<string>(widget, EfSchemaVersion.ColumnName)).SingleAsync());
        Assert.Equal(EfSchemaFinalization.SchemaVersion, await reader.Set<EfSchemaFinalizationRecordRow>().Select(row => row.SchemaVersion).SingleAsync());
        Assert.Equal("2", (await new EfSchemaFinalizationStore(reader).FindAsync(Family))!.Intent!.Version);
    }

    private static readonly IEqualityComparer<SchemaFinalizationRecord> RecordComparer = new RecordEquality();

    private static void AssertSkew(EfSchemaVersionSkewException skew) =>
        EfSchemaVersionSkewTestSupport.AssertSchemaVersionSkew(skew, EfSchemaFinalization.SchemaFamily, EfSchemaFinalization.SchemaVersion);

    private static async Task<SchemaFinalizationRefusedException> AssertRefusedAsync(SchemaFinalizationRefusal refusal, Func<Task> write)
    {
        var refused = await Assert.ThrowsAsync<SchemaFinalizationRefusedException>(write);
        Assert.Equal(refusal, refused.Refusal);
        Assert.Equal(Family, refused.Family);
        return refused;
    }

    private static SchemaFinalizationRecord Applied(SchemaFinalizationWrite write)
    {
        Assert.True(write.Applied, "The write lost its compare-and-set.");
        return write.Record;
    }

    private Task<SchemaFinalizationRecord> CreatedAsync() => Store().GetOrCreateAsync(Family, "1", Chain, Operator);

    private async Task<SchemaFinalizationRecord> FinalizedAsync(SchemaFinalizationRecord record, string version)
    {
        var intended = Applied(await Store().RecordIntentAsync(Family, record.Revision, version, Chain, HostA));
        return Applied(await Store().CommitIntentAsync(Family, intended.Revision, Chain, HostA));
    }

    private async Task<SchemaFinalizationRecord> FoundAsync() =>
        await Store().FindAsync(Family) ?? throw new InvalidOperationException("The record is gone.");

    /// <summary>Every column of the record row, so a refused write can be shown to have changed nothing at all.</summary>
    private async Task<string> RawAsync()
    {
        var row = await Context().Set<EfSchemaFinalizationRecordRow>().AsNoTracking().SingleAsync(candidate => candidate.Family == Family);
        return string.Join('|', row.SchemaVersion, row.DatabaseIdentity, row.Revision, row.FinalizedVersion, row.IntentJson,
            row.HoldsJson, row.HistoryJson, row.FinishJson, row.FinishHistoryJson);
    }

    private EfSchemaFinalizationStore Store(params IInterceptor[] interceptors) => new(Context(interceptors), new FixedClock(Now));

    private FinalizationContext Context(params IInterceptor[] interceptors)
    {
        var context = new FinalizationContext(new DbContextOptionsBuilder<FinalizationContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(interceptors)
            .Options);
        contexts.Add(context);
        return context;
    }

    private async Task DisposeContextsAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        contexts.Clear();
    }

    private sealed class FinalizationContext(DbContextOptions<FinalizationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.MapSchemaFinalization("ElsaFinalizationTests").IndexSchemaVersionStamps();
    }

    private sealed class VersionedContext(DbContextOptions<VersionedContext> options) : DbContext(options), IEfSchemaVersionedContext
    {
        public const string Version = "9.9.9";

        private static readonly EfSchemaChain SchemaChain =
            EfSchemaChain.For(new EfSchemaFamilyDescriptor("Versioned", null, Version, typeof(VersionedContext).Assembly));

        EfSchemaChain IEfSchemaVersionedContext.SchemaChain => SchemaChain;

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EfSchemaVersionMaterializationInterceptor.StampWrites(this);
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => EfSchemaVersionMaterializationInterceptor.EnsureAdded(optionsBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var widget = modelBuilder.Entity<Widget>();
            widget.ToTable("widgets");
            widget.HasKey(row => row.Id);
            widget.Property<string>(EfSchemaVersion.ColumnName).HasMaxLength(32).IsRequired();
            modelBuilder.MapSchemaFinalization("ElsaVersionedTests").IndexSchemaVersionStamps();
        }
    }

    private sealed class Widget
    {
        public string Id { get; set; } = "";
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Runs one competing write the first time the context it is installed on starts to save.</summary>
    private sealed class BeforeFirstSave(Func<Task<SchemaFinalizationWrite>> write) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                Applied(await write());
            }

            return result;
        }
    }

    /// <summary>Record equality by value, lists included, which the records' own equality compares by reference.</summary>
    private sealed class RecordEquality : IEqualityComparer<SchemaFinalizationRecord>
    {
        public bool Equals(SchemaFinalizationRecord? x, SchemaFinalizationRecord? y) =>
            x is not null && y is not null &&
            x with { Holds = [], History = [], FinishHistory = [] } == y with { Holds = [], History = [], FinishHistory = [] } &&
            x.Holds.SequenceEqual(y.Holds) && x.History.SequenceEqual(y.History) && x.FinishHistory.SequenceEqual(y.FinishHistory);

        public int GetHashCode(SchemaFinalizationRecord obj) => HashCode.Combine(obj.Family, obj.Revision);
    }
}
