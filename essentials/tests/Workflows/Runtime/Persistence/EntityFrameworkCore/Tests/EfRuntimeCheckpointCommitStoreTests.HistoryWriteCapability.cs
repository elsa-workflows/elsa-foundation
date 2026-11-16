using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void Unfaithful_effective_history_member_or_comparer_cannot_qualify_pairing(bool independent, bool comparer)
    {
        using RuntimeDbContext context = comparer
            ? new IgnoredHistoryChangeContext(new DbContextOptionsBuilder<IgnoredHistoryChangeContext>().UseSqlite("Data Source=:memory:").Options)
            : new AliasedHistoryContentContext(new DbContextOptionsBuilder<AliasedHistoryContentContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess();
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(independent ? observer.CanRecordHistory(reader) : writer.CanCommitHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Aliased_effective_content_member_loses_the_owned_content_on_actual_sqlite_save_and_read()
    {
        await using var context = new AliasedHistoryContentContext(new DbContextOptionsBuilder<AliasedHistoryContentContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
        context.Set<RuntimeHistoryEventEntity>().Add(new()
        {
            Id = "event", ScopeKey = "tenant-a", ScopeKeyHash = new string('a', 64), WorkflowExecutionId = "workflow-a",
            WorkflowExecutionIdHash = new string('b', 64), ContentJson = "{\"event\":true}", SchemaVersion = "1.0.0", Revision = 1,
            HistoryEventId = "history-event", HistoryEventIdHash = new string('c', 64), Name = "ActivityStarted",
            NameHash = new string('d', 64), CandidateFingerprint = new string('e', 64), Sequence = 1
        });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var restored = await context.Set<RuntimeHistoryEventEntity>().SingleAsync();
        Assert.Null(restored.ContentJson); Assert.Equal("ActivityStarted", restored.Name);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        var table = context.Model.FindEntityType(typeof(RuntimeHistoryEventEntity))!.GetTableName();
        command.CommandText = $"SELECT ContentJson FROM {table}";
        Assert.Equal("ActivityStarted", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task All_equal_effective_content_comparer_keeps_old_json_when_actual_bookmark_status_advances()
    {
        await using var context = new IgnoredHistoryChangeContext(new DbContextOptionsBuilder<IgnoredHistoryChangeContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
        var bookmark = new RuntimeHistoryBookmarkEntity
        {
            Id = "bookmark", ScopeKey = "tenant-a", ScopeKeyHash = new string('a', 64), WorkflowExecutionId = "workflow-a",
            WorkflowExecutionIdHash = new string('b', 64), ContentJson = "{\"state\":\"active\"}", SchemaVersion = "1.0.0", Revision = 1,
            BookmarkId = "bookmark-a", BookmarkIdHash = new string('c', 64), ActivityExecutionId = "activity-a",
            ActivityExecutionIdHash = new string('d', 64), FirstRecordedSequence = 1, Status = 0
        };
        context.Set<RuntimeHistoryBookmarkEntity>().Add(bookmark); await context.SaveChangesAsync();
        bookmark.ContentJson = "{\"state\":\"resumed\"}"; bookmark.Status = 1; bookmark.Revision++;
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var restored = await context.Set<RuntimeHistoryBookmarkEntity>().SingleAsync();
        Assert.Equal(1, restored.Status); Assert.Equal(2, restored.Revision);
        Assert.Equal("{\"state\":\"active\"}", restored.ContentJson);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Owned_CLR_property_access_keeps_native_and_independent_history_pairing(bool independent)
    {
        using var context = new PropertyHistoryAccessContext(new DbContextOptionsBuilder<PropertyHistoryAccessContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess();
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        var comparers = context.Model.GetEntityTypes().Where(entity => RuntimeHistoryEfModule.Chain.ContentColumns.Any(column => column.Entity == entity.ClrType))
            .SelectMany(entity => entity.GetProperties()).Where(property => ((Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty)property).GetValueComparer() is { } comparer &&
                !ReferenceEquals(comparer, property.GetTypeMapping().Comparer))
            .Select(property => $"{property.DeclaringType.Name}.{property.Name}: {property.GetValueComparer().GetType().FullName} vs {property.GetTypeMapping().Comparer.GetType().FullName}");
        Assert.True(independent ? observer.CanRecordHistory(reader) : writer.CanCommitHistory(reader), string.Join("; ", comparers));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class PropertyHistoryAccessContext(DbContextOptions<PropertyHistoryAccessContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var entity in RuntimeHistoryEfModule.Chain.ContentColumns.Select(column => column.Entity).Distinct())
                builder.Entity(entity).UsePropertyAccessMode(PropertyAccessMode.Property);
        }
    }

    private sealed class AliasedHistoryContentContext(DbContextOptions<AliasedHistoryContentContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var history = builder.Entity<RuntimeHistoryEventEntity>();
            history.Property(row => row.Name).Metadata.FieldInfo = null;
            history.Property(row => row.Name).UsePropertyAccessMode(PropertyAccessMode.Property);
            history.Property(row => row.ContentJson).HasField("<Name>k__BackingField").UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }

    private sealed class IgnoredHistoryChangeContext(DbContextOptions<IgnoredHistoryChangeContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryBookmarkEntity>().Property(row => row.ContentJson).Metadata.SetValueComparer(
                new ValueComparer<string>((left, right) => true, value => 0, value => value));
        }
    }
}
