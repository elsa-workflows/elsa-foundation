using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Theory]
    [InlineData("head")] [InlineData("page")] [InlineData("event")] [InlineData("event-details")]
    [InlineData("bookmark")] [InlineData("bookmark-details")] [InlineData("bookmarks")] [InlineData("occurrences")]
    public async Task Every_selected_read_checks_native_ownership_before_history_projections(string operation)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"), new("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        var commands = new Commands(); await using var context = db.Open(commands); await Invoke(db.Store(context), operation);
        var nativeTable = context.Model.FindEntityType(typeof(WorkflowExecutionStateEntity))!.GetTableName()!;
        Assert.Contains(nativeTable, commands.Texts[0], StringComparison.Ordinal);
        Assert.Contains(commands.Texts.Skip(1), text => text.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("head")] [InlineData("page")] [InlineData("event")] [InlineData("event-details")]
    [InlineData("bookmark")] [InlineData("bookmark-details")] [InlineData("bookmarks")] [InlineData("occurrences")]
    public async Task Missing_native_execution_never_discloses_or_even_reads_orphaned_history(string operation)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"), new("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await using (var remove = db.Open()) await remove.WorkflowExecutionStates.ExecuteDeleteAsync();
        var commands = new Commands(); await using var context = db.Open(commands); var value = await Invoke(db.Store(context), operation);
        AssertMissing(value, operation);
        Assert.Single(commands.Texts); Assert.DoesNotContain("elsa_runtime_history_", commands.Texts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_native_run_without_admitted_capture_cannot_borrow_authority_from_orphaned_history()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"));
        await using (var corrupt = db.Open())
        {
            var row = await corrupt.WorkflowExecutionStates.SingleAsync(); var state = EfWorkflowExecutionStateStore.ReadChecked(row, "scope", "run");
            var next = EfWorkflowExecutionStateStore.ToEntity(state with { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false }, "scope", row.Id, row.Revision + 1);
            corrupt.Entry(row).CurrentValues.SetValues(next); await corrupt.SaveChangesAsync();
        }
        var commands = new Commands(); await using var context = db.Open(commands); var page = await db.Store(context).ReadOccurrencesAsync(new("run"));
        Assert.Empty(page.Items); Assert.Null(page.Head); Assert.Equal(RuntimeHistoryOccurrenceAvailability.Unavailable, page.Availability);
        Assert.Single(commands.Texts);
    }

    [Fact]
    public async Task Native_and_history_admission_policy_disagreement_is_hard_integrity()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"));
        await db.ChangeHead(head => head with { Policy = new("other", 1, true, Policy.SupportedKinds) });
        await using var context = db.Open(); await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).FindHeadAsync("run").AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_complete_initial_inventory_detects_missing_rows_or_overstated_active_counts(bool missingRow)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("b1-created", "a", "BookmarkCreated", Mutation: Mutation("b-1", "a")),
            new("b2-created", "a", "BookmarkCreated", Mutation: Mutation("b-2", "a")));
        await using (var corrupt = db.Open())
        {
            if (missingRow) await corrupt.Set<RuntimeHistoryBookmarkEntity>().Where(row => row.BookmarkId == EfRelationalIdentity.Encode("b-1")).ExecuteDeleteAsync();
            else
            {
                var row = await corrupt.Set<RuntimeHistoryBookmarkCountEntity>().SingleAsync(item => item.ActivityExecutionId == null);
                var next = new Mapping(Chain).Row("scope", "run", new Mapping.BookmarkCount(null, 3, 3)); next.Revision = row.Revision + 1;
                corrupt.Entry(row).CurrentValues.SetValues(next); await corrupt.SaveChangesAsync();
            }
        }
        await using var context = db.Open(); await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ListBookmarksAsync(new("run")).AsTask());
    }

    private static void AssertMissing(object? value, string operation)
    {
        switch (operation)
        {
            case "head": case "event": case "bookmark": Assert.Null(value); break;
            case "event-details": var item = Assert.IsType<(RuntimeHistoryHead?, RuntimeHistoryEvent?)>(value); Assert.Null(item.Item1); Assert.Null(item.Item2); break;
            case "bookmark-details": var bookmark = Assert.IsType<(RuntimeHistoryHead?, RuntimeHistoryBookmark?)>(value); Assert.Null(bookmark.Item1); Assert.Null(bookmark.Item2); break;
            case "page": var page = Assert.IsType<RuntimeHistoryPage>(value); Assert.Null(page.Head); Assert.Empty(page.Items); Assert.Equal(0, page.UpperSequence); break;
            case "bookmarks": var books = Assert.IsType<RuntimeHistoryBookmarkPage>(value); Assert.Null(books.Head); Assert.Empty(books.Items); Assert.Null(books.TotalCount); break;
            default: var occurrences = Assert.IsType<RuntimeHistoryOccurrencePage>(value); Assert.Null(occurrences.Head); Assert.Empty(occurrences.Items); Assert.Null(occurrences.TotalCount); break;
        }
    }
}
