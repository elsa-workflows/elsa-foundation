using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities
{
    internal sealed class MessageActiveInterestEntity
    {
        public string Id { get; set; } = null!;
        public string ScopeKey { get; set; } = null!;
        public string ScopeKeyHash { get; set; } = null!;
        public string? TenantId { get; set; }
        public string? TenantIdHash { get; set; }
        public string WorkflowExecutionId { get; set; } = null!;
        public string WorkflowExecutionIdHash { get; set; } = null!;
        public string Incarnation { get; set; } = null!;
        public string DescriptorFingerprint { get; set; } = null!;
        public bool Closed { get; set; }
        public string ContentJson { get; set; } = null!;
        public string SchemaVersion { get; set; } = null!;
        public long Revision { get; set; }
    }
}
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration
{
    internal sealed class MessageActiveInterestEntityConfiguration : IEntityTypeConfiguration<MessageActiveInterestEntity>
    {
        public void Configure(EntityTypeBuilder<MessageActiveInterestEntity> b)
        {
            RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeMessageInterestEfModule.ActiveTable);
            b.Property(row => row.ScopeKey).HasMaxLength(null); b.HasKey(row => row.Id); b.Property(row => row.Id).HasMaxLength(64);
            b.Property(row => row.TenantIdHash).HasMaxLength(64); b.Property(row => row.WorkflowExecutionId).IsRequired();
            b.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired(); b.Property(row => row.Incarnation).HasMaxLength(32).IsRequired();
            b.Property(row => row.DescriptorFingerprint).IsRequired(); b.HasIndex(row => new { row.ScopeKeyHash, row.TenantIdHash, row.Closed, row.WorkflowExecutionIdHash });
        }
    }
}
