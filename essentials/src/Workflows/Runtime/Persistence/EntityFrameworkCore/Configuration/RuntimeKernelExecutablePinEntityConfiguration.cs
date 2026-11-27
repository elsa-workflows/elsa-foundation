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
    }
}
