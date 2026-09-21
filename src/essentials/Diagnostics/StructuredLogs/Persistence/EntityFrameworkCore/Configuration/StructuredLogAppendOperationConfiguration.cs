using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Configuration;

public sealed class StructuredLogAppendOperationConfiguration : IEntityTypeConfiguration<StructuredLogAppendOperation>
{
    public void Configure(EntityTypeBuilder<StructuredLogAppendOperation> builder)
    {
        builder.ToTable(StructuredLogsEfModule.AppendOperationsTableName);
        builder.HasKey(operation => new { operation.ScopeKey, operation.BatchId });
        builder.Property(operation => operation.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.BatchId).HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.ScopeId).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.StreamId).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.IssuedAtTicks).IsRequired();
        builder.Property(operation => operation.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.OutcomeJson).IsRequired();
        builder.HasIndex(operation => new { operation.ScopeKey, operation.IssuedAtTicks });
    }
}
