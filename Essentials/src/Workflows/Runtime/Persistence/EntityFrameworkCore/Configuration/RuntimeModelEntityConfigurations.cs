using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeModelExecutionEntityConfiguration : IEntityTypeConfiguration<RuntimeModelExecutionEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeModelExecutionEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeModelStateEfModule.StateTable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RuntimeModelExecutionEntity.ExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeModelExecutionEntity.ExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RuntimeModelExecutionEntity.ExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeModelExecutionEntity.ArtifactIdHash));
        b.HasIndex(x => new { x.ScopeKeyHash, x.ExecutionIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ExecutionIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash });
    }
}

internal sealed class RuntimeModelInputEntityConfiguration : IEntityTypeConfiguration<RuntimeModelInputEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeModelInputEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeModelStateEfModule.InputTable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RuntimeModelInputEntity.ExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeModelInputEntity.ExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RuntimeModelInputEntity.InputId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeModelInputEntity.InputIdHash));
        b.HasIndex(x => new { x.ScopeKeyHash, x.ExecutionIdHash, x.InputIdHash }).IsUnique();
    }
}
