using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

/// <summary>Private projection model; production registration joins the generated migration slice.</summary>
internal static class RuntimeHistoryEntityConfiguration
{
    private static void Common<T>(EntityTypeBuilder<T> b, string table) where T : class
    {
        b.ToTable(table);
        b.HasKey("Id");
        b.Property<string>("Id").HasMaxLength(64).IsRequired();
        b.Property<string>("ScopeKey").IsRequired();
        b.Property<string>("ScopeKeyHash").HasMaxLength(64).IsRequired();
        b.Property<string>("WorkflowExecutionId").IsRequired();
        b.Property<string>("WorkflowExecutionIdHash").HasMaxLength(64).IsRequired();
        b.Property<string>("ContentJson").IsRequired();
        b.Property<string>("SchemaVersion").HasMaxLength(32).IsRequired();
        b.Property<long>("Revision").IsConcurrencyToken();
    }

    internal static void Configure(ModelBuilder model)
    {
        var a = model.Entity<RuntimeHistoryActivityEntity>();
        Common(a, RuntimeHistoryProjectionSchema.ActivityTable);
        a.Property(x => x.ActivityExecutionId).IsRequired();
        a.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        a.Property(x => x.ParentActivityExecutionId).IsRequired(false);
        a.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityExecutionIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_activity_owner");
        var h = model.Entity<RuntimeHistoryHeadEntity>();
        Common(h, RuntimeHistoryProjectionSchema.HeadTable);
        h.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_head_scope_run");
        var e = model.Entity<RuntimeHistoryEventEntity>();
        Common(e, RuntimeHistoryProjectionSchema.EventTable);
        e.Property(x => x.HistoryEventId).IsRequired();
        e.Property(x => x.HistoryEventIdHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.Name).IsRequired();
        e.Property(x => x.NameHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.CandidateFingerprint).HasMaxLength(64).IsRequired();
        e.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64);
        e.Property(x => x.BookmarkIdHash).HasMaxLength(64);
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.Sequence }).IsUnique().HasDatabaseName("IX_runtime_history_event_sequence");
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.HistoryEventIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_event_id");
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.NameHash, x.Sequence }).HasDatabaseName("IX_runtime_history_event_kind");
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.Category, x.Sequence }).HasDatabaseName("IX_runtime_history_event_category");
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityExecutionIdHash, x.Sequence }).HasDatabaseName("IX_runtime_history_event_activity");
        e.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.BookmarkIdHash, x.Sequence }).HasDatabaseName("IX_runtime_history_event_bookmark");
        var b = model.Entity<RuntimeHistoryBookmarkEntity>();
        Common(b, RuntimeHistoryProjectionSchema.BookmarkTable);
        b.Property(x => x.BookmarkId).IsRequired();
        b.Property(x => x.BookmarkIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ActivityExecutionId).IsRequired();
        b.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.BookmarkIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_bookmark_id");
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.FirstRecordedSequence }).IsUnique().HasDatabaseName("IX_runtime_history_bookmark_sequence");
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityExecutionIdHash, x.FirstRecordedSequence }).HasDatabaseName("IX_runtime_history_bookmark_activity");
        var c = model.Entity<RuntimeHistoryBookmarkCountEntity>();
        Common(c, RuntimeHistoryProjectionSchema.BookmarkCountTable);
        c.Property(x => x.ActivityExecutionId).IsRequired(false);
        c.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        c.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityExecutionIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_bookmark_count_owner");
    }
}
