using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowExecutableEntityConfiguration : IEntityTypeConfiguration<WorkflowExecutableEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutableEntity> b)
    {
        b.ToTable(RuntimeArtifactEfModule.WorkflowExecutableTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArtifactId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArtifactIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ArtifactId }).IsUnique();
    }
}
public sealed class WorkflowExecutableCoordinationEntityConfiguration : IEntityTypeConfiguration<WorkflowExecutableCoordinationEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutableCoordinationEntity> b)
    {
        b.ToTable(RuntimeArtifactEfModule.WorkflowExecutableCoordinationTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArtifactId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ArtifactId }).IsUnique();
    }
}
public sealed class ExecutableActivityTemplateEntityConfiguration : IEntityTypeConfiguration<ExecutableActivityTemplateEntity>
{
    public void Configure(EntityTypeBuilder<ExecutableActivityTemplateEntity> b)
    {
        b.ToTable(RuntimeArtifactEfModule.ExecutableActivityTemplateTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateId).HasMaxLength(128).IsRequired();
        b.Property(x => x.TemplateIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateHash).HasMaxLength(450).IsRequired();
        b.Property(x => x.TemplateIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TemplateIdHash, x.TemplateId }).IsUnique();
    }
}
public sealed class ExecutableActivityTemplateHashClaimEntityConfiguration : IEntityTypeConfiguration<ExecutableActivityTemplateHashClaimEntity>
{
    public void Configure(EntityTypeBuilder<ExecutableActivityTemplateHashClaimEntity> b)
    {
        b.ToTable(RuntimeArtifactEfModule.ExecutableActivityTemplateHashClaimTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateHash).HasMaxLength(450).IsRequired();
        b.Property(x => x.TemplateHashHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TemplateHashHash, x.TemplateHash }).IsUnique();
    }
}
public sealed class WorkflowExecutableSourceReferenceEntityConfiguration : IEntityTypeConfiguration<WorkflowExecutableSourceReferenceEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutableSourceReferenceEntity> b)
    {
        b.ToTable(RuntimeArtifactEfModule.SourceReferenceTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(128).IsRequired();
        b.Property(x => x.SourceReferenceId).HasMaxLength(128).IsRequired();
        b.Property(x => x.SourceReferenceIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ArtifactId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DefinitionVersionId).HasMaxLength(128).IsRequired();
        b.Property(x => x.DefinitionVersionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(32).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.DefinitionVersionIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.IsRetired, x.ExpiresAtUtcTicks });
    }
}
