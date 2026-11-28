using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>The immutable Runtime receipt for a committed actor Event, including an empty selection.</summary>
internal sealed class RuntimeAuthoredEventCohortEntity
{
    public string Id { get; set; } = null!;
    public string SourceFingerprint { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
}

internal sealed class RuntimeAuthoredEventCohortConfiguration : IEntityTypeConfiguration<RuntimeAuthoredEventCohortEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeAuthoredEventCohortEntity> builder)
    {
        builder.ToTable("elsa_runtime_authored_event_cohort");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
    }
}
