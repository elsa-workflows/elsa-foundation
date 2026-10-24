using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

internal abstract class EtlRuntimeDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<EtlRuntimeRunRecord> Runs => Set<EtlRuntimeRunRecord>();
    internal DbSet<EtlConnectorRunRecord> ConnectorRuns => Set<EtlConnectorRunRecord>();
    internal DbSet<EtlConnectorPartitionRecord> ConnectorPartitions => Set<EtlConnectorPartitionRecord>();
    internal DbSet<EtlConnectorBatchRecord> ConnectorBatches => Set<EtlConnectorBatchRecord>();
    internal DbSet<EtlConnectorHttpRequestRecord> ConnectorHttpRequests => Set<EtlConnectorHttpRequestRecord>();
    internal DbSet<EtlConnectorHttpPageRecord> ConnectorHttpPages => Set<EtlConnectorHttpPageRecord>();
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

        var connectorRuns = modelBuilder.Entity<EtlConnectorRunRecord>();
        connectorRuns.ToTable(EtlRuntimeEfModule.ConnectorRunTableName, table =>
        {
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_RetainedByteCounts",
                "RetainedInputBytes >= 0 AND RetainedMetadataBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_RetainedOutputBytes",
                "RetainedOutputBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_SourceInputReservedBytes",
                "SourceInputReservedBytes >= 0");
        });
        connectorRuns.HasKey(row => new { row.ScopeKey, row.RunKey });
        connectorRuns.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorRuns.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        connectorRuns.Property(row => row.BindingsJson).IsRequired();
        connectorRuns.Property(row => row.PreparedJson);
        connectorRuns.Property(row => row.SourceInputReservedBytes).HasDefaultValue(0L);
        connectorRuns.Property(row => row.RetainedOutputBytes).HasDefaultValue(0L);
        connectorRuns.Property(row => row.Revision).IsConcurrencyToken();
        connectorRuns.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        connectorRuns.HasOne<EtlRuntimeRunRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey })
            .OnDelete(DeleteBehavior.Restrict);

        var connectorPartitions = modelBuilder.Entity<EtlConnectorPartitionRecord>();
        connectorPartitions.ToTable(EtlRuntimeEfModule.ConnectorPartitionTableName, table =>
        {
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_NonNegativeCounters",
                "PageCount >= 0 AND PageBytes >= 0 AND MetadataBytes >= 0 AND BatchMetadataBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_SourceInputReservedBytes",
                "SourceInputReservedBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_NonNegativeAcceptanceCounts",
                "AcceptedBatchCount >= 0 AND AcceptedRecords >= 0 AND RejectedRecords >= 0 AND FilteredRecords >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_RetainedOutputBytes",
                "RetainedOutputBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_NotBeforeUtcTicks",
                "NotBeforeUtcTicks IS NULL OR NotBeforeUtcTicks > 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_CurrentRequestPointer",
                "(CurrentRequestSequence IS NULL AND CurrentRequestHash IS NULL) OR (CurrentRequestSequence IS NOT NULL AND CurrentRequestHash IS NOT NULL)");
        });
        connectorPartitions.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal });
        connectorPartitions.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorPartitions.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        connectorPartitions.Property(row => row.PositionJson).IsRequired();
        connectorPartitions.Property(row => row.SourceInputReservedBytes).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.Owner).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength);
        connectorPartitions.Property(row => row.CurrentRequestHash).HasMaxLength(EtlRuntimeEfModule.HashLength);
        connectorPartitions.Property(row => row.OwedBatchId).HasMaxLength(EtlRuntimeEfModule.HashLength);
        connectorPartitions.Property(row => row.RetainedOutputBytes).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.AcceptedBatchCount).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.AcceptedRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.RejectedRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.FilteredRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.Revision).IsConcurrencyToken();
        connectorPartitions.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        connectorPartitions.HasIndex(row => new { row.ScopeKey, row.RunKey, row.Completed, row.ExpiresAtUtcTicks });
        connectorPartitions.HasIndex(row => new { row.ScopeKey, row.RunKey, row.Completed, row.NotBeforeUtcTicks, row.ExpiresAtUtcTicks });
        connectorPartitions.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.CurrentRequestSequence, row.CurrentRequestHash });
        connectorPartitions.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.OwedBatchId });
        connectorPartitions.HasOne<EtlConnectorRunRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey })
            .OnDelete(DeleteBehavior.Restrict);
        connectorPartitions.HasOne<EtlConnectorHttpRequestRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.CurrentRequestSequence, row.CurrentRequestHash })
            .HasPrincipalKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        connectorPartitions.HasOne<EtlConnectorBatchRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.OwedBatchId })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        var connectorBatches = modelBuilder.Entity<EtlConnectorBatchRecord>();
        connectorBatches.ToTable(EtlRuntimeEfModule.ConnectorBatchTableName, table =>
        {
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_OutputPlan",
                "(OutputJson IS NULL AND OutputDigest IS NULL) OR (OutputJson IS NOT NULL AND OutputDigest IS NOT NULL AND length(OutputDigest) = 64)");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_HttpDeliveryPlan",
                "(HttpDeliveryJson IS NULL AND HttpDeliveryDigest IS NULL) OR (HttpDeliveryJson IS NOT NULL AND HttpDeliveryDigest IS NOT NULL AND length(HttpDeliveryDigest) = 64 AND OutputJson IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_OutputBytes",
                "OutputBytes >= 0 AND (OutputJson IS NOT NULL OR OutputBytes = 0)");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_MainFilePublishedRequiresOutput",
                "MainFilePublished = 0 OR OutputJson IS NOT NULL");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_ErrorFilePublishedRequiresOutput",
                "ErrorFilePublished = 0 OR OutputJson IS NOT NULL");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_BatchOrdinal",
                "BatchOrdinal >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_HttpPageSequence",
                "HttpPageSequence IS NULL OR HttpPageSequence >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_AcceptanceMetadataBytes",
                "AcceptanceMetadataBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_MainEffectKind",
                "MainEffectKind IS NULL OR MainEffectKind IN (1, 2)");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_ErrorEffectKind",
                "ErrorEffectKind IS NULL OR ErrorEffectKind IN (1, 2)");
        });
        connectorBatches.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.BatchId });
        connectorBatches.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorBatches.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        connectorBatches.Property(row => row.BatchId).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorBatches.Property(row => row.StartKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorBatches.Property(row => row.DescriptorJson).IsRequired();
        connectorBatches.Property(row => row.DescriptorDigest).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorBatches.Property(row => row.OutputDigest).HasMaxLength(EtlRuntimeEfModule.HashLength);
        connectorBatches.Property(row => row.HttpDeliveryDigest).HasMaxLength(EtlRuntimeEfModule.HashLength);
        connectorBatches.Property(row => row.MainEffectKind).HasColumnType("INTEGER");
        connectorBatches.Property(row => row.ErrorEffectKind).HasColumnType("INTEGER");
        connectorBatches.Property(row => row.BatchOrdinal).HasDefaultValue(0L);
        connectorBatches.Property(row => row.AcceptanceMetadataBytes).HasDefaultValue(0L);
        connectorBatches.Property(row => row.MainFilePublished).HasDefaultValue(false);
        connectorBatches.Property(row => row.ErrorFilePublished).HasDefaultValue(false);
        connectorBatches.Property(row => row.OutputBytes).HasDefaultValue(0L);
        connectorBatches.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        connectorBatches.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.StartKey }).IsUnique();
        connectorBatches.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.BatchOrdinal }).IsUnique();
        connectorBatches.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.HttpPageSequence });
        connectorBatches.HasIndex(row => new
        {
            row.ScopeKey, row.RunKey, row.Settled, row.PartitionOrdinal, row.MainEffectKind, row.ErrorEffectKind
        });
        connectorBatches.HasOne<EtlConnectorPartitionRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal })
            .OnDelete(DeleteBehavior.Restrict);

        var connectorHttpRequests = modelBuilder.Entity<EtlConnectorHttpRequestRecord>();
        connectorHttpRequests.ToTable(EtlRuntimeEfModule.ConnectorHttpRequestTableName, table =>
        {
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_requests_NonNegativeMetadataBytes",
                "MetadataBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_requests_SourceFetchMetadata",
                "FetchMetadataBytes >= 0 AND SourceMetadataReserveBytes >= 0 AND InputReservationBytes >= 0 AND " +
                "((FetchJson IS NULL AND FetchDigest IS NULL AND FetchMetadataBytes = 0 AND SourceMetadataReserveBytes = 0 AND InputReservationBytes = 0) OR " +
                "(FetchJson IS NOT NULL AND FetchDigest IS NOT NULL AND length(FetchDigest) = 64))");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_requests_SequencePredecessor",
                "(PageSequence = 0 AND PreviousPageSequence IS NULL AND PreviousHasContinuation IS NULL) OR (PageSequence > 0 AND PreviousPageSequence IS NOT NULL AND PreviousHasContinuation IS NOT NULL AND PreviousPageSequence = PageSequence - 1 AND PreviousHasContinuation = 1)");
        });
        connectorHttpRequests.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence });
        connectorHttpRequests.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpRequests.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        connectorHttpRequests.Property(row => row.RequestHash).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpRequests.Property(row => row.RequestJson).IsRequired();
        connectorHttpRequests.Property(row => row.FetchDigest).HasMaxLength(EtlRuntimeEfModule.HashLength);
        connectorHttpRequests.Property(row => row.FetchMetadataBytes).HasDefaultValue(0L);
        connectorHttpRequests.Property(row => row.SourceMetadataReserveBytes).HasDefaultValue(0L);
        connectorHttpRequests.Property(row => row.InputReservationBytes).HasDefaultValue(0L);
        connectorHttpRequests.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        connectorHttpRequests.HasAlternateKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash })
            .HasName("AK_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PageSequence_RequestHash");
        connectorHttpRequests.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.RequestHash }).IsUnique();
        connectorHttpRequests.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PreviousPageSequence, row.RequestHash, row.PreviousHasContinuation });
        connectorHttpRequests.HasOne<EtlConnectorPartitionRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal })
            .OnDelete(DeleteBehavior.Restrict);
        connectorHttpRequests.HasOne<EtlConnectorHttpPageRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PreviousPageSequence, row.RequestHash, row.PreviousHasContinuation })
            .HasPrincipalKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.NextRequestHash, row.HasContinuation })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        var connectorHttpPages = modelBuilder.Entity<EtlConnectorHttpPageRecord>();
        connectorHttpPages.ToTable(EtlRuntimeEfModule.ConnectorHttpPageTableName, table =>
        {
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_pages_NonNegativeByteCounts",
                "ByteLength >= 0 AND MetadataBytes >= 0");
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_pages_AcceptedItemCount",
                "AcceptedItemCount >= 0");
        });
        connectorHttpPages.HasKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence });
        connectorHttpPages.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpPages.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        connectorHttpPages.Property(row => row.RequestHash).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpPages.Property(row => row.NextRequestHash).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpPages.Property(row => row.PageJson).IsRequired();
        connectorHttpPages.Property(row => row.PageDigest).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        connectorHttpPages.Property(row => row.AcceptedItemCount).HasDefaultValue(0);
        connectorHttpPages.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();
        connectorHttpPages.HasAlternateKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.NextRequestHash, row.HasContinuation })
            .HasName("AK_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_PageSequence_NextRequestHash_HasContinuation");
        connectorHttpPages.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.RequestHash }).IsUnique();
        connectorHttpPages.HasIndex(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash });
        connectorHttpPages.HasOne<EtlConnectorPartitionRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal })
            .OnDelete(DeleteBehavior.Restrict);
        connectorHttpPages.HasOne<EtlConnectorHttpRequestRecord>()
            .WithMany()
            .HasForeignKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash })
            .HasPrincipalKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash })
            .OnDelete(DeleteBehavior.Restrict);

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
