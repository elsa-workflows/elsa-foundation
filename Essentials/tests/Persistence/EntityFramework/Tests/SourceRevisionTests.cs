using Elsa.Persistence.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class SourceRevisionTests
{
    private static readonly SourceScope Agent = new("partition", "tenant", "agent", "studio");
    private static readonly SourceScope Workflow = Agent with { Kind = "workflow" };
    private sealed class SourceContext(string connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) => model.AddSourceRevisions("source", Database.ProviderName!);
    }
    private static async Task<SourceContext> Open(TemporarySqliteDatabase file)
    {
        var db = new SourceContext(file.ConnectionString);
        await db.Database.EnsureCreatedAsync(); return db;
    }

    [Fact]
    public async Task Domain_boundaries_and_case_sensitive_source_identity_do_not_alias()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        foreach (var scope in new[] { Agent, Workflow, Agent with { TenantId = null }, Agent with { TenantId = "other" }, Agent with { Partition = "other" } })
        {
            var store = new EfSourceRevisions(db, scope);
            await store.CreateAsync("same", "draft", "Copywriter", "agent/1", "{}");
            await store.CreateAsync("SAME", "draft", "Other", "agent/1", "{}");
            Assert.Equal(2, (await store.ListAsync(null, 0, 20)).Total);
        }
        Assert.Null(await new EfSourceRevisions(db, Agent with { TenantId = "absent" }).FindAsync("same", "draft"));
        Assert.Equal(10, await db.Set<SourceWorkingCopy>().CountAsync());
        var config = new EfSourceRevisions(db, Agent with { Authority = "config" });
        Assert.Null(await config.FindAsync("same", "draft"));
        Assert.Equal("identity-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => config.CreateAsync("same", "draft", "Copywriter", "agent/1", "{}"))).Code);
    }

    [Fact]
    public async Task Real_cas_rejects_stale_save_and_capture_without_overwriting_the_winner()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Workflow);
        await store.CreateAsync("flow", "draft", "Flow", "workflow/1", "old");
        await store.SaveAsync("flow", "draft", 0, "Flow", "workflow/1", "winner");
        Assert.Equal("revision-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.SaveAsync("flow", "draft", 0, "Flow", "workflow/1", "loser"))).Code);
        Assert.Equal("revision-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.CaptureAsync("flow", "draft", 0, "stale"))).Code);
        Assert.Equal("winner", (await store.FindAsync("flow", "draft"))!.Content);
        Assert.Empty(await store.HistoryAsync("flow", "draft"));
    }

    [Fact]
    public async Task Capture_retry_after_lost_reply_and_later_edit_returns_original_snapshot_and_pin()
    {
        await using var file = new TemporarySqliteDatabase();
        string snapshot;
        await using (var db = await Open(file))
        {
            var store = new EfSourceRevisions(db, Agent);
            await store.CreateAsync("copywriter", "draft", "Copywriter", "agent/1", "reviewed");
            snapshot = (await store.CaptureAsync("copywriter", "draft", 0, "publish-1")).Key;
        }
        await using (var db = await Open(file))
        {
            var store = new EfSourceRevisions(db, Agent);
            await store.SaveAsync("copywriter", "draft", 0, "Copywriter", "agent/1", "later");
            var replay = await store.CaptureAsync("copywriter", "draft", 0, "publish-1");
            Assert.Equal(snapshot, replay.Key); Assert.Equal("reviewed", replay.Content);
            Assert.Equal("operation-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.CaptureAsync("copywriter", "draft", 1, "publish-1"))).Code);
            await store.DiscardAsync("copywriter", "draft", 1);
            Assert.Equal(snapshot, (await store.CaptureAsync("copywriter", "draft", 0, "publish-1")).Key);
            Assert.Equal("not-found", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.CaptureAsync("copywriter", "draft", 2, "new-operation"))).Code);
            Assert.Single(await store.HistoryAsync("copywriter", "draft"));
            Assert.Equal(1, await db.Set<SourceSnapshotPin>().CountAsync());
        }
    }

    [Fact]
    public async Task Pins_and_discard_retain_immutable_history_until_references_are_released()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Agent);
        await store.CreateAsync("agent", "draft", "Agent", "agent/1", "{}");
        var snapshot = await store.CaptureAsync("agent", "draft", 0, "review");
        await store.PinAsync("agent", "draft", snapshot.Key, "release-1.0.0");
        await store.UnpinAsync("agent", "draft", snapshot.Key, "capture:review");
        Assert.Equal("snapshot-pinned", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.DeleteSnapshotAsync("agent", "draft", snapshot.Key))).Code);
        await store.DiscardAsync("agent", "draft", 0);
        Assert.Null(await store.FindAsync("agent", "draft"));
        Assert.Single(await store.HistoryAsync("agent", "draft"));
        Assert.Equal(0, (await store.ListAsync(null, 0, 10)).Total);
        await store.UnpinAsync("agent", "draft", snapshot.Key, "release-1.0.0");
        await store.DeleteSnapshotAsync("agent", "draft", snapshot.Key);
        Assert.Empty(await store.HistoryAsync("agent", "draft"));
    }

    [Fact]
    public async Task Snapshot_content_cannot_be_updated_even_by_an_ef_consumer()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Workflow);
        await store.CreateAsync("flow", "draft", "Flow", "workflow/1", "original");
        var snapshot = await store.CaptureAsync("flow", "draft", 0, "capture");
        snapshot.Content = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Fork_keeps_provenance_and_cannot_capture_another_sources_snapshot()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Agent);
        await store.CreateAsync("origin", "draft", "Origin", "agent/1", "retained");
        var snapshot = await store.CaptureAsync("origin", "draft", 0, "review");
        var fork = await store.ForkAsync("origin", "draft", snapshot.Key, "fork", "draft", "Fork");
        Assert.Equal(snapshot.Key, fork.BaseSnapshotId);
        Assert.Equal("retained", fork.Content);
        Assert.Equal("not-found", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.ForkAsync("fork", "draft", snapshot.Key, "forged", "draft", "Bad"))).Code);
        await store.UnpinAsync("origin", "draft", snapshot.Key, "capture:review");
        Assert.Equal("snapshot-pinned", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.DeleteSnapshotAsync("origin", "draft", snapshot.Key))).Code);
    }

    [Fact]
    public async Task Source_count_and_paging_include_unpublished_drafts_and_empty_pages()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Agent);
        for (var i = 0; i < 5; i++) await store.CreateAsync("agent-" + i, "draft", i < 3 ? "Writer" : "Reviewer", "agent/1", "{}");
        var first = await store.ListAsync("writer", 0, 2);
        var second = await store.ListAsync("writer", 2, 2);
        var empty = await store.ListAsync("writer", 10, 2);
        Assert.Equal(3, first.Total); Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, second.Total); Assert.Single(second.Items);
        Assert.Equal(3, empty.Total); Assert.Empty(empty.Items);
        Assert.Empty(first.Items.Select(x => x.Key).Intersect(second.Items.Select(x => x.Key)));
    }
    [Fact]
    public async Task Independent_contexts_enforce_commit_time_cas_and_preserve_losing_local_content()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var left = await Open(file);
        var store = new EfSourceRevisions(left, Workflow);
        await store.CreateAsync("flow", "draft", "Flow", "workflow/1", "original");
        await using var right = await Open(file);
        var a = await left.Set<SourceWorkingCopy>().SingleAsync();
        var b = await right.Set<SourceWorkingCopy>().SingleAsync();
        EfSourceRevisions.StageSave(left, a, Workflow, "flow", "draft", 0, "Flow", "workflow/1", "winner");
        EfSourceRevisions.StageSave(right, b, Workflow, "flow", "draft", 0, "Flow", "workflow/1", "local loser");
        await left.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.SaveChangesAsync());
        Assert.Equal("local loser", b.Content);
        Assert.Equal("winner", (await store.FindAsync("flow", "draft"))!.Content);
    }

    [Fact]
    public async Task Store_does_not_commit_unrelated_pending_changes_in_a_consumer_context()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Agent);
        await store.CreateAsync("agent", "draft", "Agent", "agent/1", "original");
        (await db.Set<SourceWorkingCopy>().SingleAsync()).Content = "pending";
        Assert.Equal("pending-changes", (await Assert.ThrowsAsync<SourceRevisionException>(() => store.CaptureAsync("agent", "draft", 0, "review"))).Code);
        Assert.True(db.ChangeTracker.HasChanges());
        await using var check = await Open(file);
        Assert.Equal("original", (await check.Set<SourceWorkingCopy>().SingleAsync()).Content);
        Assert.Empty(await check.Set<SourceSnapshot>().ToListAsync());
    }

    [Fact]
    public async Task Domain_single_copy_claim_is_atomic_while_workflow_allows_multiple_copies()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var agent = new EfSourceRevisions(db, Agent, singleCopyPerSource: true);
        await agent.CreateAsync("agent", "one", "Agent", "agent/1", "{}");
        Assert.Equal("identity-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => agent.CreateAsync("agent", "two", "Other", "agent/1", "{}"))).Code);
        await agent.DiscardAsync("agent", "one", 0);
        await agent.CreateAsync("agent", "two", "New copy", "agent/1", "{}");
        Assert.Single((await agent.ListAsync(null, 0, 10)).Items);
        var workflow = new EfSourceRevisions(db, Workflow);
        await workflow.CreateAsync("flow", "one", "Flow", "workflow/1", "{}");
        await workflow.CreateAsync("flow", "two", "Flow", "workflow/1", "{}");
        Assert.Equal(2, (await workflow.ListAsync(null, 0, 10)).Total);
    }

    [Theory]
    [InlineData("source_copies")]
    [InlineData("source_snapshots")]
    public async Task Shared_rows_refuse_future_schema_before_returning_content(string table)
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = await Open(file);
        var store = new EfSourceRevisions(db, Agent);
        await store.CreateAsync("agent", "draft", "Agent", "agent/1", "original");
        await store.CaptureAsync("agent", "draft", 0, "capture");
        // The table comes only from the fixed theory cases, never from authored input.
        await db.Database.ExecuteSqlRawAsync("UPDATE " + table + " SET SchemaVersion = '99.0.0'");
        if (table == "source_copies")
            await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.FindAsync("agent", "draft"));
        else
            await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.HistoryAsync("agent", "draft"));
    }

}

// Independent typed consumer of the neutral ports: no Workflow/Agent DTO, inheritance or JSON CRUD service.
public sealed class TypedSourceConsumerTests
{
    private sealed record Note(string Subject, int Priority);
    private sealed class Notes(ISourceRevisionReader reader, ISourceRevisionWriter writer)
    {
        public async Task Save(string id, long? revision, Note note)
        {
            var content = System.Text.Json.JsonSerializer.Serialize(note);
            if (revision is null) await writer.CreateAsync(id, "working", note.Subject, "notes/1", content);
            else await writer.SaveAsync(id, "working", revision.Value, note.Subject, "notes/1", content);
        }
        public async Task<Note?> Read(string id)
        {
            var row = await reader.FindAsync(id, "working");
            return row is null ? null : System.Text.Json.JsonSerializer.Deserialize<Note>(row.Content);
        }
    }
    private sealed class NotesContext(string connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) => model.AddSourceRevisions("notes", Database.ProviderName!);
    }
    [Fact]
    public async Task Independent_typed_consumer_uses_the_same_ports_and_storage()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = new NotesContext(file.ConnectionString);
        await db.Database.EnsureCreatedAsync();
        var store = new EfSourceRevisions(db, new("authoring", "tenant", "notes", "notes-owner"));
        var notes = new Notes(store, store);
        await notes.Save("one", null, new("First", 1));
        var snapshot = await store.CaptureAsync("one", "working", 0, "review");
        await notes.Save("one", 0, new("Edited", 2));
        Assert.Equal(new("Edited", 2), await notes.Read("one"));
        Assert.Equal(new("First", 1), System.Text.Json.JsonSerializer.Deserialize<Note>((await store.FindSnapshotAsync("one", "working", snapshot.Key))!.Content));
        Assert.Null(await store.FindSnapshotAsync("other", "working", snapshot.Key));
        Assert.Single(await store.HistoryAsync("one", "working"));
    }
}
