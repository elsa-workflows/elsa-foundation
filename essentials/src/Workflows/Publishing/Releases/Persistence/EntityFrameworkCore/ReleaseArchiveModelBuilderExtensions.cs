using Elsa.Persistence.EntityFramework;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

/// <summary>Installs only archive mappings in an ordinary consumer-owned context. The host owns schema preparation.</summary>
public static class ReleaseArchiveModelBuilderExtensions
{
    internal const string SchemaFamily = "WorkflowReleaseArchives";
    internal const string SchemaVersion = "1.0.0";

    public static ModelBuilder AddReleaseArchiveModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        EnsureProvider(providerName);
        foreach (var method in typeof(ArchiveSqliteFunctions).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            modelBuilder.HasDbFunction(method).HasName(method.Name == nameof(ArchiveSqliteFunctions.Character) ? "char" : "typeof").IsBuiltIn();
        var archive = modelBuilder.Entity<ReleaseArchiveRow>();
        archive.ToTable("elsa_release_archives");
        archive.HasKey(x => x.Key);
        archive.Property(x => x.Manifest).HasColumnType("BLOB").IsRequired();
        archive.Property(x => x.ManifestDigest).HasMaxLength(64).IsRequired();
        archive.Property(x => x.ReleaseDigest).IsRequired();
        archive.Property(x => x.CreatedAt).IsRequired();
        var entry = modelBuilder.Entity<ReleaseArchiveEntryRow>();
        entry.ToTable("elsa_release_archive_entries");
        entry.HasKey(x => x.Key);
        entry.Property(x => x.Content).HasColumnType("BLOB").IsRequired();
        entry.Property(x => x.ContentDigest).HasMaxLength(64).IsRequired();
        entry.Property(x => x.Name).IsRequired();
        entry.HasIndex(x => new { x.ScopeKey, x.ArchiveKey });
        entry.HasOne<ReleaseArchiveRow>().WithMany().HasForeignKey(x => x.ArchiveKey).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in new[] { archive.Metadata, entry.Metadata })
        {
            foreach (var name in new[] { "Key", "ScopeKey", "Authority", "Partition", "TenantId", "Kind", "CaptureId", "SchemaVersion" })
            {
                var property = entity.FindProperty(name)!;
                property.IsNullable = name == "TenantId";
                if (name is "Key" or "ScopeKey") property.SetMaxLength(64);
                if (name == "SchemaVersion") property.SetMaxLength(32);
            }
            foreach (var property in entity.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName,
            [typeof(ReleaseArchiveRow), typeof(ReleaseArchiveEntryRow)],
            "Key", "ArchiveKey", "ScopeKey", "Authority", "Partition", "TenantId", "Kind", "CaptureId", "Name", "ReleaseDigest", "ManifestDigest", "ContentDigest", "SchemaVersion");
        return modelBuilder;
    }

    internal static void EnsureProvider(string? providerName)
    {
        if (providerName != EfProviderNames.Sqlite)
            throw new NotSupportedException("The release archive store currently supports the SQLite provider only.");
    }
}
