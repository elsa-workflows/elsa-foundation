using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations
{
    /// <summary>
    /// EF Core configuration for <see cref="WorkflowDefinitionDraftLayout"/>. FK to
    /// <c>WorkflowDefinitionDraft</c> with <c>OnDelete(Cascade)</c> per research item R5 —
    /// Draft deletion via <c>IDiscardDraftCommand</c> (Unit C FR-029) MUST atomically delete
    /// the layout sibling. Records are serialized as JSON via System.Text.Json.
    /// </summary>
    internal sealed class WorkflowDefinitionDraftLayoutConfiguration : IEntityTypeConfiguration<WorkflowDefinitionDraftLayout>
    {
        public void Configure(EntityTypeBuilder<WorkflowDefinitionDraftLayout> builder)
        {
            builder.HasKey(x => x.Id);

            builder
                .HasOne(x => x.WorkflowDefinitionDraft)
                .WithOne()
                .HasForeignKey<WorkflowDefinitionDraftLayout>(x => x.WorkflowDefinitionDraftId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .Property(x => x.Records)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<DesignMetadataRecord>>(v, (JsonSerializerOptions?)null) ?? new List<DesignMetadataRecord>(),
                    new ValueComparer<ICollection<DesignMetadataRecord>>(
                        (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                        v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
                        v => v.ToList()
                    )
                )
                .HasColumnType("TEXT")
                .HasMaxLength(-1);
        }
    }
}
