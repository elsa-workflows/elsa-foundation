using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations;

/// <summary>
/// EF Core configuration for <see cref="WorkflowDefinitionDraftValidation"/>. FK to
/// <see cref="WorkflowDefinitionDraft"/> with <c>OnDelete(Cascade)</c> per Unit C FR-029 —
/// discarding a Draft atomically drops its validation sibling. Errors are serialised as JSON
/// (mirrors the Layout sibling's <c>Records</c> mapping).
/// </summary>
internal sealed class WorkflowDefinitionDraftValidationConfiguration : IEntityTypeConfiguration<WorkflowDefinitionDraftValidation>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionDraftValidation> builder)
    {
        builder.HasKey(x => x.Id);

        builder
            .HasOne(x => x.WorkflowDefinitionDraft)
            .WithOne()
            .HasForeignKey<WorkflowDefinitionDraftValidation>(x => x.WorkflowDefinitionDraftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Property(x => x.Errors)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ValidationError>>(v, (JsonSerializerOptions?)null) ?? new List<ValidationError>(),
                new ValueComparer<List<ValidationError>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
                    v => v.ToList()
                )
            )
            .HasColumnType("TEXT")
            .HasMaxLength(-1);
    }
}
