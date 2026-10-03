using System.Globalization;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>
/// The module's shared model. Each provider-derived context below owns its own migration set, so the module itself
/// references no database engine: the host brings the one it selects with the <c>ef-provider</c> capability.
/// </summary>
public abstract partial class RenewalsDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<RenewalRecord> Renewals => Set<RenewalRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.Entity<RenewalRecord>(entity =>
        {
            entity.ToTable(RenewalsModule.TableName);
            entity.HasKey(renewal => renewal.Id);
            entity.Property(renewal => renewal.Id).HasMaxLength(64).IsRequired();
            entity.Property(renewal => renewal.PolicyReference).HasMaxLength(128).IsRequired();
            // Stored as text, so every engine, SQLite included, keeps the instant and its offset exactly.
            entity.Property(renewal => renewal.CreatedAt)
                .HasConversion(
                    value => value.ToString("O", CultureInfo.InvariantCulture),
                    value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(renewal => renewal.SchemaVersion).HasMaxLength(32).IsRequired();
            ConfigureReleaseModel(entity);
        });

        // The finalization record's tables, which every module's own baseline migration creates beside its history table.
        modelBuilder.MapSchemaFinalization(RenewalsModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
    }

    partial void ConfigureReleaseModel(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RenewalRecord> entity);
}

public sealed class RenewalsSqliteDbContext(DbContextOptions<RenewalsSqliteDbContext> options) : RenewalsDbContext(options);

public sealed class RenewalsPostgreSqlDbContext(DbContextOptions<RenewalsPostgreSqlDbContext> options) : RenewalsDbContext(options);
