using System.Globalization;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>
/// The module's shared model. Each provider-derived context below owns its own migration set, so the module itself
/// references no database engine: the host brings the one it selects with the <c>ef-provider</c> capability.
/// </summary>
public abstract class NotesDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<NoteRecord> Notes => Set<NoteRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.Entity<NoteRecord>(entity =>
        {
            entity.ToTable(NotesModule.TableName);
            entity.HasKey(note => note.Id);
            entity.Property(note => note.Id).HasMaxLength(64).IsRequired();
            entity.Property(note => note.Text).HasMaxLength(2000).IsRequired();
            // Stored as text, so every engine, SQLite included, keeps the instant and its offset exactly.
            entity.Property(note => note.CreatedAt)
                .HasConversion(
                    value => value.ToString("O", CultureInfo.InvariantCulture),
                    value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(note => note.SchemaVersion).HasMaxLength(32).IsRequired();
        });

        // The finalization record's tables, which every module's own baseline migration creates beside its history table.
        modelBuilder.MapSchemaFinalization(NotesModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
    }
}

public sealed class NotesSqliteDbContext(DbContextOptions<NotesSqliteDbContext> options) : NotesDbContext(options);

public sealed class NotesPostgreSqlDbContext(DbContextOptions<NotesPostgreSqlDbContext> options) : NotesDbContext(options);
