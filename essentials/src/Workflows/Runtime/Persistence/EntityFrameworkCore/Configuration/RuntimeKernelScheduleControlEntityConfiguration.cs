using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeKernelScheduleControlEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelScheduleControlEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelScheduleControlEntity> builder)
    {
        builder.ToTable(RuntimeKernelScheduleControlEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.TenantKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OwnerSlotHash).HasMaxLength(64);
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken();

        builder.HasIndex(row => new { row.ScopeKeyHash, row.TenantKeyHash, row.Id }).HasDatabaseName("IX_KernelScheduleControl_Tenant");
        builder.HasIndex(row => new { row.ScopeKeyHash, row.OwnerSlotHash }).HasDatabaseName("IX_KernelScheduleControl_Owner");
        builder.HasIndex(row => new { row.ScopeKeyHash, row.TenantKeyHash, row.InsertionOrdinal, row.Id })
            .HasDatabaseName("IX_KernelScheduleControl_Scan");
    }
}

internal sealed class RuntimeKernelScheduleControlScanEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelScheduleControlScanEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelScheduleControlScanEntity> builder)
    {
        builder.ToTable("elsa_runtime_kernel_schedule_control_scan");
        builder.HasKey(row => new { row.ScopeKeyHash, row.TenantKeyHash });
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64);
        builder.Property(row => row.TenantKeyHash).HasMaxLength(64);
        builder.Property(row => row.AfterId).HasMaxLength(64);
        builder.Property(row => row.LastOrdinal).IsConcurrencyToken();
        builder.Property(row => row.Pass).IsConcurrencyToken();
    }
}
