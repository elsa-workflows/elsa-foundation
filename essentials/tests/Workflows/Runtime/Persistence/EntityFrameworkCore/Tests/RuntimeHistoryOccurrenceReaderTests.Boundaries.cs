using System.Data;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using Reader = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceReader;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class RuntimeHistoryOccurrenceReaderTests
{
    [Theory]
    [InlineData("run")] [InlineData("scope")] [InlineData("limit")]
    [InlineData("access")] [InlineData("activity")] [InlineData("descendants")]
    public async Task Window_tokens_bind_complete_physical_run_filter_limit_and_server_access_context(string change)
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]);
        var initial = await db.Read(new("run", 5, activityExecutionId: "a", accessBinding: "allowed"));
        var commands = new Commands();
        var query = new RuntimeHistoryOccurrenceQuery(change == "run" ? "other" : "run", change == "limit" ? 6 : 5,
            initial.SequenceToken, activityExecutionId: change == "activity" ? "other" : "a", includeDescendants: change == "descendants",
            accessBinding: change == "access" ? "changed" : "allowed");
        await Assert.ThrowsAsync<ArgumentException>(() => db.Read(query, commands, change == "scope" ? "other" : "scope"));
        Assert.Empty(commands.Selects); Assert.Equal(0, commands.Writes);
    }

    [Fact]
    public async Task Malformed_or_wrong_purpose_tokens_fail_before_any_relational_read()
    {
        await using var db = await Database.Create();
        foreach (var value in new[] { "invalid", Codec.Encode("other-purpose", new byte[] { 1, 2 }) })
        { var commands = new Commands(); await Assert.ThrowsAsync<ArgumentException>(() => db.Read(new("run", sequenceToken: value), commands)); Assert.Empty(commands.Selects); }
    }

    [Fact]
    public async Task Policy_change_or_head_deletion_retires_previously_valid_admission()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); var admitted = await db.Read();
        await db.ChangeHead(head => head with { Policy = new("changed", 1, true, Policy.SupportedKinds) });
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, (await db.Read(new("run", sequenceToken: admitted.SequenceToken))).Availability);
        await using (var context = db.Open()) await context.Set<RuntimeHistoryHeadEntity>().ExecuteDeleteAsync();
        var missing = await db.Read(new("run", sequenceToken: admitted.SequenceToken));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, missing.Availability); Assert.Empty(missing.Items); Assert.Null(missing.TotalCount);
    }

    [Theory]
    [InlineData("missing")] [InlineData("gap")] [InlineData("unknown-kind")]
    [InlineData("late-baseline")] [InlineData("lagging")] [InlineData("missing-counter")]
    public async Task Incomplete_or_unknown_projection_never_impersonates_a_complete_empty_recording(string scenario)
    {
        await using var db = await Database.Create();
        if (scenario != "missing") await db.Append([Event(scenario == "late-baseline" ? 20 : 1, "a",
            scenario == "unknown-kind" ? "FutureActivityKind" : "ActivityStarted")]);
        if (scenario == "gap") await db.ChangeHead(head => head with { HasGaps = true });
        if (scenario == "lagging") await db.Append([Event(2, "late")], stage: false);
        if (scenario == "missing-counter")
        { await using var context = db.Open(); await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ExecuteDeleteAsync(); }
        var page = await db.Read(); Assert.Equal(RuntimeHistoryOccurrenceAvailability.Unavailable, page.Availability);
        Assert.Empty(page.Items); Assert.Null(page.TotalCount); Assert.Null(page.SequenceToken);
    }

    [Fact]
    public async Task Later_materialization_remains_outside_the_original_frozen_membership()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); var admitted = await db.Read();
        await db.Append([Event(2, "late")], stage: false);
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Unavailable, (await db.Read(new("run", sequenceToken: admitted.SequenceToken))).Availability);
        await using (var context = db.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceProjection.StageAsync(context, "scope", "run", Chain, [Event(2, "late")]);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        var frozen = await db.Read(new("run", sequenceToken: admitted.SequenceToken));
        Assert.Equal(new[] { "a" }, Ids(frozen)); Assert.Equal(1, frozen.TotalCount); Assert.True(frozen.HasNewerHistory);
        Assert.Equal(new[] { "a", "late" }, Ids(await db.Read()));
    }

    [Fact]
    public async Task Unknown_first_ancestry_cannot_be_repaired_into_a_known_descendant_window()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "root"), Event(2, "unknown", known: false)]);
        await db.Append([Event(3, "unknown", "ActivityContinuationStarted", "root")]);
        var page = await db.Read(new("run", activityExecutionId: "root", includeDescendants: true));
        Assert.Equal("scope-ancestry-unknown", page.Reason); Assert.Empty(page.Items);
        Assert.Equal(new[] { "root", "unknown" }, Ids(await db.Read()));
    }

    [Theory]
    [InlineData("counter-content")] [InlineData("membership-content")] [InlineData("event-content")]
    [InlineData("missing-event")] [InlineData("missing-member")] [InlineData("receipt-content")]
    public async Task Corrupt_or_missing_exact_rows_are_integrity_failures_instead_of_omitted_steps(string scenario)
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a"), Event(2, "b"), Event(3, "c")]);
        await using (var context = db.Open())
        {
            if (scenario == "counter-content") (await context.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).ContentJson = "{}";
            if (scenario == "membership-content") (await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().SingleAsync(row => row.ActivityScopeId == null && row.Ordinal == 1)).ContentJson = "{}";
            if (scenario == "event-content") (await context.Set<RuntimeHistoryEventEntity>().SingleAsync(row => row.Sequence == 2)).ContentJson = "{}";
            if (scenario == "receipt-content") (await context.Set<RuntimeHistoryOccurrenceEntity>().SingleAsync(row => row.FirstSequence == 2)).ContentJson = "{}";
            if (scenario == "missing-event") context.Remove(await context.Set<RuntimeHistoryEventEntity>().SingleAsync(row => row.Sequence == 2));
            if (scenario == "missing-member") context.Remove(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().SingleAsync(row => row.ActivityScopeId == null && row.Ordinal == 1));
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Read());
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public async Task Boundary_member_loss_cannot_shrink_or_rebase_a_frozen_or_retained_sequence(bool partial, bool frozen, bool last)
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a"), Event(2, "b"), Event(3, "c")]);
        if (partial) await db.PurgeProjection(1);
        var admission = await db.Read(); await db.Append([Event(4, "d")]);
        var ordinal = last ? frozen ? 2 : 3 : partial ? 1 : 0;
        await using (var context = db.Open())
            await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().Where(row => row.ActivityScopeId == null && row.Ordinal == ordinal).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Read(new("run", sequenceToken: frozen ? admission.SequenceToken : null)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_newer_content_family_stamp_remains_schema_skew_not_corruption(bool counter)
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]);
        await using (var context = db.Open())
        { if (counter) (await context.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).SchemaVersion = "9.0.0";
          else (await context.Set<RuntimeHistoryEventEntity>().SingleAsync()).SchemaVersion = "9.0.0"; await context.SaveChangesAsync(); }
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => db.Read());
    }

    [Fact]
    public async Task A_clean_serializable_caller_transaction_is_preserved_and_remains_usable()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); await using var context = db.Open();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.Equal(new[] { "a" }, Ids(await Reader.ReadAsync(context, "scope", Chain, Codec, new("run"))));
        Assert.Same(transaction, context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<RuntimeHistoryEventEntity>().CountAsync()); await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Dirty_context_and_ambient_transaction_are_rejected_without_clearing_the_callers_state()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); await using var context = db.Open();
        var added = new Mapping(Chain).Row("scope", Event(2, "b"), new string('a', 64)); context.Add(added);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reader.ReadAsync(context, "scope", Chain, Codec, new("run")).AsTask());
        Assert.Equal(EntityState.Added, context.Entry(added).State); context.Entry(added).State = EntityState.Detached;
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reader.ReadAsync(context, "scope", Chain, Codec, new("run")).AsTask());
    }

    [Fact]
    public async Task Cancellation_and_real_provider_failure_are_not_converted_to_playable_empty_results()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); await using var context = db.Open();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader.ReadAsync(context, "scope", Chain, Codec, new("run"), cancellation.Token).AsTask());
        Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
        await context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_event");
        await Assert.ThrowsAsync<SqliteException>(() => Reader.ReadAsync(context, "scope", Chain, Codec, new("run")).AsTask());
        Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
    }
}
