using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Isolated relational projection controls, not native checkpoint/provider qualification.</summary>
public sealed class RuntimeHistoryProjectionSchemaTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("projection-control", 1, true, ["ActivityStarted"]);
    private static readonly Type[] ProjectionTypes = [typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity),
        typeof(RuntimeHistoryBookmarkEntity), typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryBookmarkCountEntity)];

    [Fact]
    public void Production_runtime_model_does_not_register_the_private_tables_before_migrations()
    {
        using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        foreach (var type in ProjectionTypes) Assert.Null(context.Model.FindEntityType(type));
    }

    [Fact]
    public void Projection_keys_fit_provider_limits_without_indexing_unbounded_identities_or_content()
    {
        using var context = new ProjectionContext(new DbContextOptionsBuilder<ProjectionContext>()
            .UseSqlite("Data Source=:memory:").Options);
        Assert.Equal(ProjectionTypes.OrderBy(type => type.FullName), context.Model.GetEntityTypes().Select(type => type.ClrType).OrderBy(type => type.FullName));
        foreach (var type in context.Model.GetEntityTypes())
        {
            Assert.True(type.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.Contains(type.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(["SchemaVersion"]));
            foreach (var index in type.GetIndexes())
            {
                Assert.InRange(index.GetDatabaseName()!.Length, 1, 63);
                var maximumBytes = 0;
                foreach (var property in index.Properties)
                {
                    if (property.ClrType == typeof(string))
                    {
                        var length = property.GetMaxLength();
                        Assert.NotNull(length);
                        maximumBytes += length.Value * 4; // utf8mb4 is the widest supported indexed text here.
                    }
                    else maximumBytes += property.ClrType == typeof(long) ? 8 : 4;
                }
                Assert.InRange(maximumBytes, 1, 900);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Scoped_identity_and_sequence_keys_do_not_alias(int scenario)
    {
        var (leftScope, leftRun, rightScope, rightRun) = scenario switch
        {
            0 => ("tenant-a", "run", "tenant-b", "run"),
            1 => ("Tenant-A", "Run", "tenant-a", "run"),
            2 => ("tenant|run", "one", "tenant", "run|one"),
            // Build lone UTF-16 code units at runtime; custom-attribute UTF-8 encoding cannot retain them.
            _ => ("tenant-" + (char)0xD800, "run-" + (char)0xDC00, "tenant-\uFFFD", "run-\uFFFD")
        };
        await using var database = await ProjectionDatabase.CreateAsync();
        await using (var write = database.Open())
        {
            write.AddRange(Event(leftScope, leftRun, "event", 1), Event(rightScope, rightRun, "event", 1));
            await write.SaveChangesAsync();
        }
        await using var read = database.Open();
        foreach (var (scope, run) in new[] { (leftScope, leftRun), (rightScope, rightRun) })
        {
            var scopeKey = EfRelationalIdentity.Encode(scope); var runKey = EfRelationalIdentity.Encode(run);
            var row = await read.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleAsync(item => item.ScopeKey == scopeKey && item.WorkflowExecutionId == runKey);
            var observation = RuntimeArtifactJson.Deserialize<RuntimeHistoryEvent>(row.ContentJson);
            Assert.Equal(run, observation.WorkflowExecutionId);
            Assert.Equal(scope, EfRelationalIdentity.Decode(row.ScopeKey));
            Assert.Equal("event", observation.HistoryEventId);
        }
        Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_sequence_or_event_receipt_rolls_back_an_already_saved_head_reservation(bool duplicateSequence)
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        await SeedAsync(database);
        await using (var write = database.Open())
        {
            await using var transaction = await write.Database.BeginTransactionAsync();
            var head = await write.Set<RuntimeHistoryHeadEntity>().SingleAsync();
            Advance(head, 2);
            await write.SaveChangesAsync(); // Still inside the caller-owned transaction.
            var duplicate = Event("tenant", "run", duplicateSequence ? "other-event" : "event", duplicateSequence ? 1 : 2);
            // Distinct primary identity makes the event-id index, rather than the PK, reject this corrupt receipt.
            if (!duplicateSequence) duplicate.Id = EfRelationalIdentity.Hash("different-physical-row");
            write.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using var read = database.Open();
        var retained = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(1, retained.Revision);
        Assert.Equal(1, retained.LastSequence);
        Assert.Equal(1, RuntimeArtifactJson.Deserialize<RuntimeHistoryHead>(retained.ContentJson).LastSequence);
        Assert.Equal(1, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
    }

    [Fact]
    public async Task A_stale_head_revision_cannot_publish_another_event()
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        await SeedAsync(database);
        await using var first = database.Open();
        await using var stale = database.Open();
        var firstHead = await first.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        var staleHead = await stale.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Advance(firstHead, 2); first.Add(Event("tenant", "run", "event-2", 2));
        await first.SaveChangesAsync();
        Advance(staleHead, 3); stale.Add(Event("tenant", "run", "event-3", 3));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var read = database.Open();
        Assert.Equal(2, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
        Assert.Equal(new long[] { 1, 2 }, await read.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).Select(row => row.Sequence).ToArrayAsync());
    }

    [Fact]
    public async Task Content_retirement_can_retain_the_exact_event_receipt_without_reusing_its_position()
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        await SeedAsync(database);
        string id; string fingerprint;
        await using (var retire = database.Open())
        {
            var row = await retire.Set<RuntimeHistoryEventEntity>().SingleAsync();
            id = row.Id; fingerprint = row.CandidateFingerprint;
            row.ContentJson = ""; row.IsPurged = true; row.Revision++;
            await retire.SaveChangesAsync();
        }
        await using var read = database.Open();
        var receipt = await read.Set<RuntimeHistoryEventEntity>().SingleAsync(row => row.Id == id);
        Assert.True(receipt.IsPurged); Assert.Empty(receipt.ContentJson);
        Assert.Equal(fingerprint, receipt.CandidateFingerprint); Assert.Equal(1, receipt.Sequence);
        Assert.Equal("event", EfRelationalIdentity.Decode(receipt.HistoryEventId));
        read.Add(Event("tenant", "run", "replacement", 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => read.SaveChangesAsync());
    }

    private static RuntimeHistoryEventEntity Event(string scope, string run, string eventId, long sequence)
    {
        var item = new RuntimeHistoryEvent(eventId, run, sequence, RuntimeHistoryEventCategory.ActivityLifecycle,
            "ActivityStarted", Now, "activity") { Authority = RuntimeHistoryAuthority.CommittedTransition,
            PhysicalCommitId = "physical", RecordedAt = Now, OccurredAtKnown = false, ActivityAncestryKnown = true };
        return new()
        {
            Id = ActivityExecutionEfSupport.CreateId("history:event", scope, run, eventId), ScopeKey = EfRelationalIdentity.Encode(scope),
            ScopeKeyHash = EfRelationalIdentity.Hash(scope), WorkflowExecutionId = EfRelationalIdentity.Encode(run), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(run),
            ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = "1.0.0", Revision = 1,
            HistoryEventId = EfRelationalIdentity.Encode(eventId), HistoryEventIdHash = EfRelationalIdentity.Hash(eventId),
            Name = EfRelationalIdentity.Encode(item.Name), NameHash = EfRelationalIdentity.Hash(item.Name), Category = (int)item.Category,
            CandidateFingerprint = new string('A', 64), ActivityExecutionId = EfRelationalIdentity.Encode("activity"),
            ActivityExecutionIdHash = EfRelationalIdentity.Hash("activity"), Sequence = sequence
        };
    }

    private static RuntimeHistoryHead Head(long sequence) => new("run", 1, sequence, "physical", Now, Policy, true, false, 0);

    private static void Advance(RuntimeHistoryHeadEntity row, long sequence)
    {
        row.LastSequence = sequence; row.Revision++;
        row.ContentJson = RuntimeArtifactJson.Serialize(Head(sequence));
    }

    private static async Task SeedAsync(ProjectionDatabase database)
    {
        await using var context = database.Open();
        context.Add(new RuntimeHistoryHeadEntity
        {
            Id = ActivityExecutionEfSupport.CreateId("history:head", "tenant", "run", ""), ScopeKey = EfRelationalIdentity.Encode("tenant"),
            ScopeKeyHash = EfRelationalIdentity.Hash("tenant"), WorkflowExecutionId = EfRelationalIdentity.Encode("run"),
            WorkflowExecutionIdHash = EfRelationalIdentity.Hash("run"), ContentJson = RuntimeArtifactJson.Serialize(Head(1)),
            SchemaVersion = "1.0.0", Revision = 1, FirstSequence = 1, LastSequence = 1
        });
        context.Add(Event("tenant", "run", "event", 1));
        await context.SaveChangesAsync();
    }

    private sealed class ProjectionContext(DbContextOptions<ProjectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            RuntimeHistoryEntityConfiguration.Configure(builder);
            builder.IndexSchemaVersionStamps();
        }
    }

    private sealed class ProjectionDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        internal static async Task<ProjectionDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new ProjectionDatabase(connection);
            await using var context = database.Open();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        internal ProjectionContext Open() => new(new DbContextOptionsBuilder<ProjectionContext>().UseSqlite(connection).Options);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
