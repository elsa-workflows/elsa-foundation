using Elsa.Persistence.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework;

/// <summary>Installs shared revision rows in an ordinary consumer-owned DbContext.</summary>
public static class EfSourceModel
{
    internal const string SchemaFamily = "SourceRevisions";
    internal const string SchemaVersion = "1.0.0";
    internal static bool Maps(Type type) => type == typeof(SourceWorkingCopy) || type == typeof(SourceSnapshot) ||
        type == typeof(SourceSnapshotPin);
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(EfSourceModel).Assembly, SchemaFamily);
    public static ModelBuilder AddSourceRevisions(this ModelBuilder model, string prefix, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var copy = model.Entity<SourceWorkingCopy>();
        copy.ToTable(prefix + "_copies");
        copy.Property<string>(EfSchemaVersion.ColumnName).HasMaxLength(32).IsRequired().HasDefaultValue(SchemaVersion);
        copy.HasKey(x => x.Key);
        copy.Property(x => x.Key).HasMaxLength(64);
        copy.Property(x => x.SourceIdentity).HasMaxLength(64);
        copy.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        copy.Property(x => x.BaseSnapshotId).HasMaxLength(64).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        copy.HasOne<SourceSnapshot>().WithMany().HasForeignKey(x => x.BaseSnapshotId).OnDelete(DeleteBehavior.Restrict);
        copy.Property(x => x.Revision).IsConcurrencyToken();
        copy.Property(x => x.Fence).IsConcurrencyToken();
        copy.Property(x => x.Name).HasMaxLength(512).IsRequired();
        copy.Property(x => x.NameSearchKey).HasMaxLength(512).IsRequired();
        copy.Property(x => x.Profile).HasMaxLength(128).IsRequired();
        foreach (var name in new[] { "Partition", "TenantId", "Kind", "Authority", "SourceId", "CopyId" })
        {
            var property = copy.Property<string>(name).HasMaxLength(256);
            if (name != "TenantId") property.IsRequired();
            property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
        copy.HasIndex(x => new { x.ScopeKey, x.Discarded, x.Key });
        copy.HasIndex(x => x.SourceIdentity).IsUnique();
        var snapshot = model.Entity<SourceSnapshot>();
        snapshot.ToTable(prefix + "_snapshots");
        snapshot.Property<string>(EfSchemaVersion.ColumnName).HasMaxLength(32).IsRequired().HasDefaultValue(SchemaVersion);
        snapshot.HasKey(x => x.Key);
        snapshot.Property(x => x.BaseSnapshotId).HasMaxLength(64);
        snapshot.HasOne<SourceSnapshot>().WithMany().HasForeignKey(x => x.BaseSnapshotId).OnDelete(DeleteBehavior.Restrict);
        snapshot.Property(x => x.Key).HasMaxLength(64);
        snapshot.Property(x => x.WorkingCopyKey).HasMaxLength(64).IsRequired();
        snapshot.Property(x => x.OperationId).HasMaxLength(256).IsRequired();
        snapshot.Property(x => x.Profile).HasMaxLength(128).IsRequired();
        snapshot.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        snapshot.HasIndex(x => new { x.WorkingCopyKey, x.Revision, x.Key });
        snapshot.HasOne<SourceWorkingCopy>().WithMany().HasForeignKey(x => x.WorkingCopyKey).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in snapshot.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        var pin = model.Entity<SourceSnapshotPin>();
        pin.ToTable(prefix + "_pins");
        pin.Property<string>(EfSchemaVersion.ColumnName).HasMaxLength(32).IsRequired().HasDefaultValue(SchemaVersion);
        pin.HasKey(x => x.Key);
        pin.Property(x => x.Key).HasMaxLength(64);
        pin.Property(x => x.SnapshotKey).HasMaxLength(64).IsRequired();
        pin.Property(x => x.Owner).HasMaxLength(512).IsRequired();
        pin.HasOne<SourceSnapshot>().WithMany().HasForeignKey(x => x.SnapshotKey).OnDelete(DeleteBehavior.Restrict);
        // Provider and change-tracker comparison share one declaration, including SQL Server keys.
        EfOrdinalCollation.ApplyToEntities(model, provider, new[] { typeof(SourceWorkingCopy), typeof(SourceSnapshot), typeof(SourceSnapshotPin) }, "Key", "ScopeKey", "WorkingCopyKey", "SnapshotKey", "NameSearchKey", "Partition", "TenantId", "Kind", "Authority", "SourceId", "CopyId");
        return model;
    }
}
