using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Cluster.EntityFrameworkCore.Configuration;

public sealed class ClusterMemberEntityConfiguration : IEntityTypeConfiguration<ClusterMemberEntity>
{
    /// <summary>Wide enough for an opaque incarnation from any provider version; this one writes 32 characters.</summary>
    public const int IncarnationMaxLength = 64;

    public void Configure(EntityTypeBuilder<ClusterMemberEntity> builder)
    {
        builder.ToTable(ClusterMembershipEfModule.TableName);
        builder.HasKey(row => new { row.HostId, row.Incarnation });
        builder.Property(row => row.HostId).HasMaxLength(ClusterHostIdConstraints.MaximumLength).IsRequired();
        builder.Property(row => row.Incarnation).HasMaxLength(IncarnationMaxLength).IsRequired();
        builder.Property(row => row.CurrentHostId).HasMaxLength(ClusterHostIdConstraints.MaximumLength);
        builder.Property(row => row.Status).HasMaxLength(16).IsRequired();
        builder.Property(row => row.HeartbeatAtUtcTicks).IsRequired();
        builder.Property(row => row.ExpiryPeriodTicks).IsRequired();
        builder.Property(row => row.ReportJson).IsRequired();
        builder.Property(row => row.ReportRevision).IsRequired();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        // Unique over the non-null values only: every engine but SQL Server already treats nulls as distinct, and EF
        // filters a unique index on a nullable column there, so any number of displaced incarnations may coexist.
        builder.HasIndex(row => row.CurrentHostId).IsUnique();
    }
}
