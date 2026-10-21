using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

internal abstract class EtlRuntimeDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<EtlRuntimeRunRecord> Runs => Set<EtlRuntimeRunRecord>();
    internal DbSet<EtlRuntimePreparedRecord> PreparedRuns => Set<EtlRuntimePreparedRecord>();
    internal DbSet<EtlRuntimePartitionRecord> Partitions => Set<EtlRuntimePartitionRecord>();
    internal DbSet<EtlRuntimeReservationRecord> Reservations => Set<EtlRuntimeReservationRecord>();
    internal DbSet<EtlRuntimeManifestRecord> Manifest => Set<EtlRuntimeManifestRecord>();
    internal DbSet<EtlRuntimeTerminalRecord> Terminals => Set<EtlRuntimeTerminalRecord>();
    internal DbSet<EtlRuntimeOutboxRecord> Outbox => Set<EtlRuntimeOutboxRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);

        var runs = modelBuilder.Entity<EtlRuntimeRunRecord>();
        runs.ToTable(EtlRuntimeEfModule.RunTableName);
        runs.HasKey(row => new { row.ScopeKey, row.RunKey });
        runs.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        runs.Property(row => row.TenantId).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength);
        runs.Property(row => row.PersistencePartition).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        runs.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        runs.Property(row => row.RunId).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        runs.Property(row => row.InvocationFingerprint).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        runs.Property(row => row.InvocationJson).IsRequired();
        runs.Property(row => row.BindingsJson);
        runs.Property(row => row.Status).IsRequired();
        runs.Property(row => row.PreparationOwner).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength);
        runs.Property(row => row.Revision).IsConcurrencyToken();
        runs.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        runs.HasIndex(row => new { row.ScopeKey, row.Status, row.CreatedAtUtcTicks });

        var prepared = modelBuilder.Entity<EtlRuntimePreparedRecord>();
        prepared.ToTable(EtlRuntimeEfModule.PreparedTableName);
        prepared.HasKey(row => new { row.ScopeKey, row.RunKey });
        prepared.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        prepared.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        ConfigureObject(prepared.Property(row => row.SourceKey));
        ConfigureHash(prepared.Property(row => row.SourceHash));
        ConfigureObject(prepared.Property(row => row.LookupKey));
        ConfigureHash(prepared.Property(row => row.LookupHash));
        ConfigureObject(prepared.Property(row => row.SourceIndexKey));
        ConfigureHash(prepared.Property(row => row.SourceIndexHash));
        prepared.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();

        var partitions = modelBuilder.Entity<EtlRuntimePartitionRecord>();
        partitions.ToTable(EtlRuntimeEfModule.PartitionTableName);
        partitions.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal });
        partitions.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        partitions.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        partitions.Property(row => row.Owner).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength);
        partitions.Property(row => row.Revision).IsConcurrencyToken();
        partitions.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        partitions.HasIndex(row => new { row.ScopeKey, row.RunKey, row.NextOrdinal });

        var reservations = modelBuilder.Entity<EtlRuntimeReservationRecord>();
        reservations.ToTable(EtlRuntimeEfModule.ReservationTableName);
        reservations.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.StartOrdinal });
        reservations.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        reservations.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        reservations.Property(row => row.BatchId).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        reservations.Property(row => row.InputHash).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        reservations.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        reservations.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.Settled });

        var manifest = modelBuilder.Entity<EtlRuntimeManifestRecord>();
        manifest.ToTable(EtlRuntimeEfModule.ManifestTableName);
        manifest.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.BatchStartOrdinal, row.Route });
        manifest.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        manifest.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        manifest.Property(row => row.BatchId).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        ConfigureObject(manifest.Property(row => row.ChunkKey));
        ConfigureHash(manifest.Property(row => row.ContentHash));
        manifest.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        manifest.HasIndex(row => new { row.ScopeKey, row.RunKey, row.BatchId, row.Route }).IsUnique();

        var terminals = modelBuilder.Entity<EtlRuntimeTerminalRecord>();
        terminals.ToTable(EtlRuntimeEfModule.TerminalTableName);
        terminals.HasKey(row => new { row.ScopeKey, row.RunKey });
        terminals.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        terminals.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        terminals.Property(row => row.ResultId).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        terminals.Property(row => row.ResultJson).IsRequired();
        ConfigureHash(terminals.Property(row => row.ResultHash));
        terminals.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();

        var outbox = modelBuilder.Entity<EtlRuntimeOutboxRecord>();
        outbox.ToTable(EtlRuntimeEfModule.OutboxTableName);
        outbox.HasKey(row => new { row.ScopeKey, row.RunKey });
        outbox.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        outbox.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        outbox.Property(row => row.DeliveryId).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        ConfigureHash(outbox.Property(row => row.PayloadHash));
        outbox.Property(row => row.Owner).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength);
        outbox.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        outbox.HasIndex(row => new { row.ScopeKey, row.AcknowledgedAtUtcTicks, row.ExpiresAtUtcTicks });

        modelBuilder.MapSchemaFinalization(EtlRuntimeEfModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
        ConfigureProvider(modelBuilder);
    }

    private static void ConfigureHash(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();

    private static void ConfigureObject(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}

internal sealed class EtlRuntimeSqliteDbContext(DbContextOptions<EtlRuntimeSqliteDbContext> options)
    : EtlRuntimeDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}
