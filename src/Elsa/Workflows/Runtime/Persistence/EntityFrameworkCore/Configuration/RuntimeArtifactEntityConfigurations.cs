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
        b.Property(x => x.ArtifactId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArtifactHash).HasMaxLength(RuntimeArtifactEfModule.HashMaximumLength).IsRequired();
        b.Property(x => x.ArtifactIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
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
        b.Property(x => x.ArtifactId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.Property(x => x.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
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
        b.Property(x => x.TemplateId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.TemplateIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateHash).HasMaxLength(RuntimeArtifactEfModule.HashMaximumLength).IsRequired();
        b.Property(x => x.TemplateHashHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.Property(x => x.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TemplateIdHash, x.TemplateId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TemplateHashHash, x.TemplateHash });
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
        b.Property(x => x.TemplateHash).HasMaxLength(RuntimeArtifactEfModule.HashMaximumLength).IsRequired();
        b.Property(x => x.TemplateHashHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TemplateId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.Property(x => x.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
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
        b.Property(x => x.SourceReferenceId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.SourceReferenceIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceReferenceIdOrderKey).HasMaxLength(655).IsRequired();
        b.Property(x => x.ArtifactId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DefinitionVersionId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.DefinitionVersionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DefinitionId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.DefinitionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKeyOrderKey).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(RuntimeArtifactEfModule.ScopeMaximumLength).IsRequired();
        b.Property(x => x.ExpiresAtUtcTicks).IsRequired(false);
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.Property(x => x.IncarnationId).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ArtifactId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.DefinitionVersionIdHash, x.DefinitionVersionId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.DefinitionIdHash, x.DefinitionId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.SourceReferenceIdHash, x.SourceReferenceId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.IsRetired, x.ExpiresAtUtcTicks });
    }
}
