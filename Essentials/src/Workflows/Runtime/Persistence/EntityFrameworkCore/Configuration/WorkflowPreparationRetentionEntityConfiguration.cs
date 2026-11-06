using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class WorkflowPreparationRetentionEntityConfiguration : IEntityTypeConfiguration<WorkflowPreparationRetentionEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowPreparationRetentionEntity> builder)
    {
        builder.ToTable(RuntimeArtifactEfModule.PreparationRetentionTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OwnerAddress).IsRequired();
        builder.Property(row => row.OperationId).IsRequired();
        // The framed logical tuple's hash supplies a bounded ordinal physical key. Full values are
        // checked against the payload on every read; a hash collision never aliases preparations.
        builder.Property(row => row.PreparationOrderKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.PreparationOrderKey }).IsUnique();
    }
}
