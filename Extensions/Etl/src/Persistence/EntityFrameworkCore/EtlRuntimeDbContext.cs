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
    internal DbSet<EtlConnectorSourceWatermarkRecord> ConnectorSourceWatermarks => Set<EtlConnectorSourceWatermarkRecord>();
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
                Constraint("{RetainedInputBytes} >= 0 AND {RetainedMetadataBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_RetainedOutputBytes",
                Constraint("{RetainedOutputBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_SourceInputReservedBytes",
                Constraint("{SourceInputReservedBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_runs_CompletionRequestedAtUtcTicks",
                Constraint("{CompletionRequestedAtUtcTicks} IS NULL OR {CompletionRequestedAtUtcTicks} > 0"));
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
                Constraint("{PageCount} >= 0 AND {PageBytes} >= 0 AND {MetadataBytes} >= 0 AND {BatchMetadataBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_SourceInputReservedBytes",
                Constraint("{SourceInputReservedBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_NonNegativeAcceptanceCounts",
                Constraint("{AcceptedBatchCount} >= 0 AND {AcceptedRecords} >= 0 AND {RejectedRecords} >= 0 AND {FilteredRecords} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_RetainedOutputBytes",
                Constraint("{RetainedOutputBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_NotBeforeUtcTicks",
                Constraint("{NotBeforeUtcTicks} IS NULL OR {NotBeforeUtcTicks} > 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_PollGeneration", Constraint("{PollGeneration} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedPageCount", Constraint("{CompactedPageCount} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedBatchCount", Constraint("{CompactedBatchCount} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedAcceptedRecords", Constraint("{CompactedAcceptedRecords} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedRejectedRecords", Constraint("{CompactedRejectedRecords} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedFilteredRecords", Constraint("{CompactedFilteredRecords} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedMainEffects", Constraint("{CompactedMainEffects} >= 0"));
            table.HasCheckConstraint("CK_elsa_etl_connector_partitions_CompactedErrorEffects", Constraint("{CompactedErrorEffects} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_partitions_CurrentRequestPointer",
                Constraint("({CurrentRequestSequence} IS NULL AND {CurrentRequestHash} IS NULL) OR ({CurrentRequestSequence} IS NOT NULL AND {CurrentRequestHash} IS NOT NULL)"));
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
        connectorPartitions.Property(row => row.PollGeneration).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedPageCount).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedBatchCount).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedAcceptedRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedRejectedRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedFilteredRecords).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedMainEffects).HasDefaultValue(0L);
        connectorPartitions.Property(row => row.CompactedErrorEffects).HasDefaultValue(0L);
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
                Constraint("({OutputJson} IS NULL AND {OutputDigest} IS NULL) OR ({OutputJson} IS NOT NULL AND {OutputDigest} IS NOT NULL AND {length}({OutputDigest}) = 64)"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_HttpDeliveryPlan",
                Constraint("({HttpDeliveryJson} IS NULL AND {HttpDeliveryDigest} IS NULL) OR ({HttpDeliveryJson} IS NOT NULL AND {HttpDeliveryDigest} IS NOT NULL AND {length}({HttpDeliveryDigest}) = 64 AND {OutputJson} IS NOT NULL)"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_OutputBytes",
                Constraint("{OutputBytes} >= 0 AND ({OutputJson} IS NOT NULL OR {OutputBytes} = 0)"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_MainFilePublishedRequiresOutput",
                Constraint("{MainFilePublished} = {false} OR {OutputJson} IS NOT NULL"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_ErrorFilePublishedRequiresOutput",
                Constraint("{ErrorFilePublished} = {false} OR {OutputJson} IS NOT NULL"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_BatchOrdinal",
                Constraint("{BatchOrdinal} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_HttpPageSequence",
                Constraint("{HttpPageSequence} IS NULL OR {HttpPageSequence} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_AcceptanceMetadataBytes",
                Constraint("{AcceptanceMetadataBytes} >= 0"));
            // The effect columns record the delivery mechanism (1 HTTP delivery, 2 file publication), not the route format.
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_MainEffectKind",
                Constraint("{MainEffectKind} IS NULL OR {MainEffectKind} IN (1, 2)"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_batches_ErrorEffectKind",
                Constraint("{ErrorEffectKind} IS NULL OR {ErrorEffectKind} IN (1, 2)"));
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
                Constraint("{MetadataBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_requests_SourceFetchMetadata",
                Constraint("{FetchMetadataBytes} >= 0 AND {SourceMetadataReserveBytes} >= 0 AND {InputReservationBytes} >= 0 AND " +
                "(({FetchJson} IS NULL AND {FetchDigest} IS NULL AND {FetchMetadataBytes} = 0 AND {SourceMetadataReserveBytes} = 0 AND {InputReservationBytes} = 0) OR " +
                "({FetchJson} IS NOT NULL AND {FetchDigest} IS NOT NULL AND {length}({FetchDigest}) = 64))"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_requests_SequencePredecessor",
                Constraint("({PreviousPageSequence} IS NULL AND {PreviousHasContinuation} IS NULL) OR ({PageSequence} > 0 AND {PreviousPageSequence} IS NOT NULL AND {PreviousHasContinuation} IS NOT NULL AND {PreviousPageSequence} = {PageSequence} - 1 AND {PreviousHasContinuation} = {true})"));
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
                Constraint("{ByteLength} >= 0 AND {MetadataBytes} >= 0"));
            table.HasCheckConstraint(
                "CK_elsa_etl_connector_http_pages_AcceptedItemCount",
                Constraint("{AcceptedItemCount} >= 0"));
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

        var watermarks = modelBuilder.Entity<EtlConnectorSourceWatermarkRecord>();
        watermarks.ToTable(EtlRuntimeEfModule.ConnectorSourceWatermarkTableName);
        watermarks.HasKey(row => new { row.ScopeKey, row.CheckpointKey });
        watermarks.Property(row => row.ScopeKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        watermarks.Property(row => row.CheckpointKey).HasMaxLength(EtlRuntimeEfModule.HashLength).IsRequired();
        watermarks.Property(row => row.RunKey).HasMaxLength(EtlRuntimeEfModule.EncodedIdentityMaximumLength).IsRequired();
        watermarks.Property(row => row.SchemaVersion).HasMaxLength(EtlRuntimeEfModule.SchemaVersionMaximumLength).IsRequired();

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

    /// <summary>
    /// The key a run id is stored under. SQLite keeps the run id's encoding; the server engines store a 64-character hash,
    /// because the encoding (up to 1,200 characters) is wider than their index limits. <c>RunId</c> always keeps the encoding.
    /// </summary>
    internal abstract string RunKeyOf(string runId);

    /// <summary>How this context's engine spells a check constraint's identifiers, length function and booleans.</summary>
    private protected abstract EtlRuntimeConstraintDialect ConstraintDialect { get; }

    /// <summary>Renders a check constraint written with <c>{Column}</c>, <c>{length}</c>, <c>{true}</c> and <c>{false}</c> tokens.</summary>
    private string Constraint(string template) => ConstraintDialect.Render(template);
}

internal sealed class EtlRuntimeSqliteDbContext(DbContextOptions<EtlRuntimeSqliteDbContext> options)
    : EtlRuntimeDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.Sqlite;
    private protected override EtlRuntimeConstraintDialect ConstraintDialect => EtlRuntimeConstraintDialect.Sqlite;
    internal override string RunKeyOf(string runId) => Stores.EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
    // Keys compare ordinally: SQLite's TEXT already does, so its model only gains the comparers.
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => EfOrdinalCollation.Apply(modelBuilder, ExpectedProviderName);
}

/// <summary>The server engines' runtime model: hashed run keys that fit their index limits, short key names, binary collation.</summary>
internal abstract class EtlRuntimeServerDbContext(DbContextOptions options) : EtlRuntimeDbContext(options)
{
    private const string RunKeyDomain = "etl-run-key-v1";

    internal override string RunKeyOf(string runId)
    {
        Stores.EtlRuntimeRecordCodec.ValidateIdentity(runId, "run id");
        return EfRelationalIdentity.HashLengthFramed(RunKeyDomain, runId);
    }

    private protected abstract string ProviderName { get; }

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            entity.FindProperty(nameof(Entities.EtlRuntimeRunRecord.RunKey))?.SetMaxLength(EtlRuntimeEfModule.HashLength);
        // MySQL allows 64 characters in a constraint name and PostgreSQL 63.
        modelBuilder.Entity<Entities.EtlConnectorHttpRequestRecord>()
            .HasAlternateKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.RequestHash })
            .HasName("AK_elsa_etl_connector_http_requests_page_request");
        modelBuilder.Entity<Entities.EtlConnectorHttpPageRecord>()
            .HasAlternateKey(row => new { row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.PageSequence, row.NextRequestHash, row.HasContinuation })
            .HasName("AK_elsa_etl_connector_http_pages_page_next_request");
        EfOrdinalCollation.Apply(modelBuilder, ProviderName);
    }
}

internal sealed class EtlRuntimeSqlServerDbContext(DbContextOptions<EtlRuntimeSqlServerDbContext> options)
    : EtlRuntimeServerDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.SqlServer;
    private protected override string ProviderName => ExpectedProviderName;
    private protected override EtlRuntimeConstraintDialect ConstraintDialect => EtlRuntimeConstraintDialect.SqlServer;
}

internal sealed class EtlRuntimePostgreSqlDbContext(DbContextOptions<EtlRuntimePostgreSqlDbContext> options)
    : EtlRuntimeServerDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.PostgreSql;
    private protected override string ProviderName => ExpectedProviderName;
    private protected override EtlRuntimeConstraintDialect ConstraintDialect => EtlRuntimeConstraintDialect.PostgreSql;
}

internal sealed class EtlRuntimeMySqlDbContext(DbContextOptions<EtlRuntimeMySqlDbContext> options)
    : EtlRuntimeServerDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.MySql;
    private protected override string ProviderName => ExpectedProviderName;
    private protected override EtlRuntimeConstraintDialect ConstraintDialect => EtlRuntimeConstraintDialect.MySql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation("MySQL:Charset", "utf8mb4");
        base.ConfigureProvider(modelBuilder);
    }
}
