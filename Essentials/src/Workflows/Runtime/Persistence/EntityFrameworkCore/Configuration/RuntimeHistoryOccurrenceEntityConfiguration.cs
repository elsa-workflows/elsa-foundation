using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

// Not installed in RuntimeDbContext until the owner's coherent four-provider migration slice.
internal static class RuntimeHistoryOccurrenceEntityConfiguration
{
    internal const string OccurrenceTable = "elsa_runtime_history_occurrence";
    internal const string MembershipTable = "elsa_runtime_history_occurrence_member";
    internal const string CounterTable = "elsa_runtime_history_occurrence_counter";

    internal static void Configure(ModelBuilder model)
    {
        var occurrence = model.Entity<RuntimeHistoryOccurrenceEntity>();
        Common(occurrence, OccurrenceTable);
        occurrence.Property(x => x.ActivityExecutionId).IsRequired();
        occurrence.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        occurrence.Property(x => x.FirstHistoryEventId).IsRequired();
        occurrence.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityExecutionIdHash })
            .IsUnique().HasDatabaseName("IX_runtime_history_occurrence_identity");
        occurrence.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.FirstSequence })
            .IsUnique().HasDatabaseName("IX_runtime_history_occurrence_first");
        occurrence.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.AncestryKnown, x.FirstSequence })
            .HasDatabaseName("IX_runtime_history_occurrence_ancestry");

        var member = model.Entity<RuntimeHistoryOccurrenceMembershipEntity>();
        Common(member, MembershipTable);
        member.Property(x => x.ScopeOwnerId).HasMaxLength(64).IsRequired();
        member.Property(x => x.ActivityExecutionId).IsRequired();
        member.Property(x => x.ActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        member.HasIndex(x => new { x.ScopeOwnerId, x.Ordinal }).IsUnique().HasDatabaseName("IX_runtime_history_occurrence_ordinal");
        member.HasIndex(x => new { x.ScopeOwnerId, x.FirstSequence }).IsUnique().HasDatabaseName("IX_runtime_history_occurrence_bound");
        member.HasIndex(x => new { x.ScopeOwnerId, x.ActivityExecutionIdHash }).IsUnique().HasDatabaseName("IX_runtime_history_occurrence_anchor");
        member.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash }).HasDatabaseName("IX_runtime_history_occurrence_member_run");

        var counter = model.Entity<RuntimeHistoryOccurrenceCounterEntity>();
        Common(counter, CounterTable);
        counter.Property(x => x.ActivityScopeIdHash).HasMaxLength(64).IsRequired();
        counter.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.ActivityScopeIdHash })
            .IsUnique().HasDatabaseName("IX_runtime_history_occurrence_counter_scope");
    }

    private static void Common<T>(EntityTypeBuilder<T> builder, string table) where T : class
    {
        builder.ToTable(table); builder.HasKey("Id");
        builder.Property<string>("Id").HasMaxLength(64).IsRequired();
        builder.Property<string>("ScopeKey").IsRequired();
        builder.Property<string>("ScopeKeyHash").HasMaxLength(64).IsRequired();
        builder.Property<string>("WorkflowExecutionId").IsRequired();
        builder.Property<string>("WorkflowExecutionIdHash").HasMaxLength(64).IsRequired();
        builder.Property<string>("ContentJson").IsRequired();
        builder.Property<string>("SchemaVersion").HasMaxLength(32).IsRequired();
        builder.Property<long>("Revision").IsConcurrencyToken();
    }
}
