using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

// SOURCE ONLY until the separately reviewed additive family mapping/migrations and old-writer
// exclusion are installed. This configuration is deliberately not auto-registered by this increment.
internal sealed class WorkflowPreparationEnrollmentEntityConfiguration : IEntityTypeConfiguration<WorkflowPreparationEnrollmentEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowPreparationEnrollmentEntity> builder)
    {
        builder.ToTable("WorkflowPreparationEnrollments");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
    }
}
