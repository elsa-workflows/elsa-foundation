using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class UserTaskConfiguration : IEntityTypeConfiguration<UserTaskEntity>
{
    public void Configure(EntityTypeBuilder<UserTaskEntity> b)
    {
        b.ToTable("UserTasks"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64);
        b.Property(x => x.ScopeHash).HasMaxLength(64);
        b.Property(x => x.ScopeKey).HasMaxLength(2048);
        b.Property(x => x.TenantHash).HasMaxLength(64);
        b.Property(x => x.TenantKey).HasMaxLength(2048);
        b.Property(x => x.TaskId).HasMaxLength(64);
        b.Property(x => x.ExecutionHash).HasMaxLength(64);
        b.Property(x => x.AssigneeHash).HasMaxLength(64);
        b.Property(x => x.GroupHash).HasMaxLength(64);
        b.Property(x => x.ClaimantHash).HasMaxLength(64);
        b.Property(x => x.ExecutionKey).HasMaxLength(4096);
        b.Property(x => x.AssigneeKey).HasMaxLength(2048);
        b.Property(x => x.GroupKey).HasMaxLength(2048);
        b.Property(x => x.ClaimantKey).HasMaxLength(2048);
        b.Property(x => x.SearchTitle).HasMaxLength(1000);
        b.Property(x => x.SchemaVersion).HasMaxLength(16);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.ScopeHash, x.TaskId }).IsUnique();
        b.HasIndex(x => new { x.ScopeHash, x.Status, x.TaskId });
        b.HasIndex(x => new { x.ScopeHash, x.AssigneeHash, x.Status, x.TaskId });
        b.HasIndex(x => new { x.ScopeHash, x.GroupHash, x.Status, x.TaskId });
        b.HasIndex(x => new { x.ScopeHash, x.ExecutionHash, x.Status });
    }
}

internal sealed class UserTaskFormConfiguration : IEntityTypeConfiguration<UserTaskFormEntity>
{
    public void Configure(EntityTypeBuilder<UserTaskFormEntity> b)
    {
        b.ToTable("UserTaskForms"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64);
        b.Property(x => x.ScopeHash).HasMaxLength(64);
        b.Property(x => x.ScopeKey).HasMaxLength(2048);
        b.Property(x => x.TenantHash).HasMaxLength(64);
        b.Property(x => x.TenantKey).HasMaxLength(2048);
        b.Property(x => x.FormKey).HasMaxLength(2048);
        b.Property(x => x.VersionKey).HasMaxLength(2048);
        b.Property(x => x.SchemaVersion).HasMaxLength(16);
        b.HasIndex(x => new { x.ScopeHash, x.TenantHash, x.Id });
    }
}
