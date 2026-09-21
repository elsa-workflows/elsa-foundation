using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Elsa.Persistence.EntityFramework;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

/// <summary>
/// Provider-neutral model of the Elsa 3 import ledger. Provider-derived contexts only bind column details;
/// nothing EF-shaped crosses the import contracts.
/// </summary>
public abstract class Elsa3ImportDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Elsa3ImportCollectionRecord> Collections => Set<Elsa3ImportCollectionRecord>();
    public DbSet<Elsa3ImportReceiptRecord> Receipts => Set<Elsa3ImportReceiptRecord>();
    public DbSet<Elsa3ImportDefinitionBindingRecord> DefinitionBindings => Set<Elsa3ImportDefinitionBindingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        ConfigureCollection(modelBuilder.Entity<Elsa3ImportCollectionRecord>());
        ConfigureReceipt(modelBuilder.Entity<Elsa3ImportReceiptRecord>());
        ConfigureDefinitionBinding(modelBuilder.Entity<Elsa3ImportDefinitionBindingRecord>());
        // Every row is immutable once written: collections and receipts are append-only and a binding is
        // provenance. An accidental update fails instead of silently rewriting history.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        ConfigureProvider(modelBuilder);
        // Installed unconditionally, so this context reads a frame whatever wrote it. Nothing here enables an
        // encoder: with no codec configured these columns are written exactly as they were before.
        modelBuilder.UseElsaPayloadColumns(
            this,
            "ContentJson");
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    /// <summary>
    /// The string columns this module compares or orders in SQL beyond the ones its composite keys already
    /// cover: the identity material the stores match on, and the hashes beside it. <c>ContentJson</c> is
    /// absent because nothing compares it in SQL; the stores compare <c>ContentHash</c> instead.
    /// </summary>
    private static readonly string[] OrdinallyComparedColumns =
    [
        "TenantKey", "TenantId", "UserId", "UserIdHash",
        "Handle", "HandleHash", "SchemaVersion", "ContentHash",
        "ReceiptId", "ReceiptIdHash", "IdempotencyKey", "CommitAttemptId",
        "BindingId", "BindingIdHash", "TargetDocumentKind",
        "TargetDefinitionId", "TargetDefinitionIdHash",
        "SourceKind", "SourceDefinitionId"
    ];

    /// <summary>Binds this module's ordinal columns to <paramref name="providerName"/>'s binary collation, per column.</summary>
    protected static void ApplyOrdinalCollation(ModelBuilder modelBuilder, string providerName) =>
        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);

    private static void ConfigureCollection(EntityTypeBuilder<Elsa3ImportCollectionRecord> builder)
    {
        builder.ToTable(Elsa3ImportEfModule.CollectionTable);
        builder.HasKey(row => new { row.TenantKey, row.UserIdHash, row.HandleHash });
        builder.Property(row => row.TenantKey).HasMaxLength(Elsa3ImportEfModule.TenantKeyLength).IsRequired();
        builder.Property(row => row.UserIdHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.HandleHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.Handle).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.TenantId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.UserId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(Elsa3ImportEfModule.SchemaVersionMaximumLength).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.ContentHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
    }

    private static void ConfigureReceipt(EntityTypeBuilder<Elsa3ImportReceiptRecord> builder)
    {
        builder.ToTable(Elsa3ImportEfModule.ReceiptTable);
        builder.HasKey(row => new { row.TenantKey, row.UserIdHash, row.ReceiptIdHash });
        builder.Property(row => row.TenantKey).HasMaxLength(Elsa3ImportEfModule.TenantKeyLength).IsRequired();
        builder.Property(row => row.UserIdHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.ReceiptIdHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.ReceiptId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.TenantId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.UserId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(Elsa3ImportEfModule.SchemaVersionMaximumLength).IsRequired();
        builder.Property(row => row.CommitAttemptId).HasMaxLength(Elsa3ImportEfModule.CommitAttemptIdLength).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.ContentHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
    }

    private static void ConfigureDefinitionBinding(EntityTypeBuilder<Elsa3ImportDefinitionBindingRecord> builder)
    {
        builder.ToTable(Elsa3ImportEfModule.DefinitionBindingTable);
        builder.HasKey(row => new { row.TenantKey, row.BindingIdHash });
        builder.Property(row => row.TenantKey).HasMaxLength(Elsa3ImportEfModule.TenantKeyLength).IsRequired();
        builder.Property(row => row.BindingIdHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.BindingId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.TargetDocumentKind).HasMaxLength(Elsa3ImportEfModule.KindMaximumLength).IsRequired();
        builder.Property(row => row.TargetDefinitionIdHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
        builder.Property(row => row.TargetDefinitionId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.SourceKind).HasMaxLength(Elsa3ImportEfModule.KindMaximumLength).IsRequired();
        builder.Property(row => row.SourceDefinitionId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.TenantId).HasMaxLength(Elsa3ImportEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.SchemaVersion).HasMaxLength(Elsa3ImportEfModule.SchemaVersionMaximumLength).IsRequired();
        builder.Property(row => row.ContentHash).HasMaxLength(Elsa3ImportEfModule.HashLength).IsRequired();
    }
}
