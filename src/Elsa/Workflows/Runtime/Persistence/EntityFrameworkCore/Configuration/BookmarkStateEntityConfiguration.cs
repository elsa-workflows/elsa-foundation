using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class BookmarkStateEntityConfiguration : IEntityTypeConfiguration<BookmarkStateEntity>
{
    public void Configure(EntityTypeBuilder<BookmarkStateEntity> builder)
    {
        builder.ToTable(BookmarkStateEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(BookmarkStateEfModule.WorkflowIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdOrderKey).HasMaxLength(BookmarkStateEfModule.OrdinalOrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.BookmarkId).HasMaxLength(BookmarkStateEfModule.BookmarkIdentityMaximumLength).IsRequired();
        builder.Property(row => row.BookmarkIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.BookmarkIdOrderKey).HasMaxLength(BookmarkStateEfModule.OrdinalOrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionId).HasMaxLength(BookmarkStateEfModule.WorkflowIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ExecutableNodeId).HasMaxLength(BookmarkStateEfModule.WorkflowIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ResumeTargetId).HasMaxLength(BookmarkStateEfModule.WorkflowIdentityMaximumLength).IsRequired();
        builder.Property(row => row.StimulusType).HasMaxLength(BookmarkStateEfModule.StimulusTypeMaximumLength).IsRequired();
        builder.Property(row => row.StimulusHash).HasMaxLength(BookmarkStateEfModule.StimulusHashMaximumLength).IsRequired();
        builder.Property(row => row.StimulusLookupKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.StimulusTypeLookupKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.PayloadJson).IsRequired(false);
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.MetadataJson).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).IsRequired();
        builder.Property(row => row.CreatedAtOffsetMinutes).IsRequired();
        builder.Property(row => row.ExpiresAtUtcTicks).IsRequired(false);
        builder.Property(row => row.ExpiresAtOffsetMinutes).IsRequired(false);
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.StimulusLookupKey });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.StimulusTypeLookupKey });
    }
}
