using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeKernelExecutablePinEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelExecutablePinEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelExecutablePinEntity> builder)
    {
        builder.ToTable(RuntimeKernelExecutablePinEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.ReleasedAtUtcTicks, row.Id }).HasDatabaseName("IX_KernelExecutablePin_Open");
        builder.HasIndex(row => new { row.ScopeKeyHash, row.ReleasedAtUtcTicks, row.InsertionOrdinal, row.Id })
            .HasDatabaseName("IX_KernelExecutablePin_Scan");
    }
}

internal sealed class RuntimeKernelExecutablePinScanEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelExecutablePinScanEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelExecutablePinScanEntity> builder)
    {
        builder.ToTable("elsa_runtime_kernel_executable_pin_scan");
        builder.HasKey(row => row.ScopeKeyHash);
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64);
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.AfterId).HasMaxLength(64);
        builder.Property(row => row.LastOrdinal).IsConcurrencyToken();
        builder.Property(row => row.Pass).IsConcurrencyToken();
    }
}
