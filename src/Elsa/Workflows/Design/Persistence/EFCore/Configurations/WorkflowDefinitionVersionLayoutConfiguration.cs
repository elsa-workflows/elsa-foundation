using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations;

/// <summary>
/// EF Core configuration for <see cref="WorkflowDefinitionVersionLayout"/>. FK to
/// <c>WorkflowDefinitionVersion</c> with <c>OnDelete(Restrict)</c> per research item R5 —
/// Versions are never deleted; the Restrict behaviour is documentary belt-and-braces.
/// Records are serialized as JSON via System.Text.Json per Unit C plan §1 + R5.
/// </summary>
internal sealed class WorkflowDefinitionVersionLayoutConfiguration : IEntityTypeConfiguration<WorkflowDefinitionVersionLayout>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionVersionLayout> builder)
    {
        builder.HasKey(x => x.Id);

        builder
            .HasOne(x => x.WorkflowDefinitionVersion)
            .WithOne()
            .HasForeignKey<WorkflowDefinitionVersionLayout>(x => x.WorkflowDefinitionVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.WorkflowDefinitionVersionId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.Records).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder
            .Property(x => x.Records)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<DesignMetadataRecord>>(v, (JsonSerializerOptions?)null) ?? new List<DesignMetadataRecord>(),
                new ValueComparer<IEnumerable<DesignMetadataRecord>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
                    v => v.ToList()
                )
            )
            .HasColumnType("TEXT")
            .HasMaxLength(-1);
    }
}