using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

/// <summary>Installs only the Weaver session, record and audit entities in an ordinary consumer-owned context.</summary>
public static class WeaverModelBuilderExtensions
{
    internal const int IdentityLength = 128;
    internal const int PrincipalLength = 256;
    internal const int HashLength = 64;
    internal const int KindLength = 32;
    internal const int SchemaVersionLength = 32;

    private static readonly Type[] Entities =
    [
        typeof(WeaverSessionRow), typeof(WeaverMessageRow), typeof(WeaverDisclosureSnapshotRow), typeof(WeaverContextRow),
        typeof(WeaverProposalRow), typeof(WeaverCallKeyRow), typeof(WeaverAuditRow), typeof(WeaverAuditOwnerRow), typeof(WeaverAuditSequenceRow)
    ];

    public static ModelBuilder AddWeaverModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        // Protected payloads are ciphertext, never JSON a provider could index or validate.
        var payloadType = providerName switch
        {
            EfProviderNames.Sqlite => "TEXT", EfProviderNames.SqlServer => "nvarchar(max)",
            EfProviderNames.PostgreSql => "text", EfProviderNames.MySql => "longtext",
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        };

        modelBuilder.Entity<WeaverSessionRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.SessionTableName);
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.TenantId).HasMaxLength(PrincipalLength).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(PrincipalLength).IsRequired();
            entity.Property(x => x.Workspace).HasMaxLength(PrincipalLength);
            entity.Property(x => x.ContinuedFrom).HasMaxLength(IdentityLength);
            entity.Property(x => x.LimitReached).HasMaxLength(KindLength);
            entity.Property(x => x.OwnerToken).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Payload).HasColumnType(payloadType).IsRequired();
            entity.Property(x => x.PausedTurnPayload).HasColumnType(payloadType);
            entity.Property(x => x.PendingAuditsPayload).HasColumnType(payloadType);
            // Owner first, then the list order and its cursor; the limit applies after both.
            entity.HasIndex(x => new { x.TenantId, x.ActorId, x.UpdatedAtUtcTicks, x.SessionId });
        });
        modelBuilder.Entity<WeaverMessageRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.MessageTableName);
            entity.HasKey(x => new { x.SessionId, x.MessageId });
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.MessageId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.StatusReason).HasMaxLength(KindLength);
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Payload).HasColumnType(payloadType).IsRequired();
            entity.HasOne<WeaverSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WeaverDisclosureSnapshotRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.SnapshotTableName);
            entity.HasKey(x => new { x.SessionId, x.Digest });
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.Digest).HasMaxLength(HashLength).IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Canonical).HasColumnType(payloadType).IsRequired();
            entity.HasOne<WeaverSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WeaverContextRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.ContextTableName);
            entity.HasKey(x => new { x.SessionId, x.AttachmentKey });
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.AttachmentKey).HasMaxLength(HashLength).IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Payload).HasColumnType(payloadType).IsRequired();
            entity.HasOne<WeaverSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WeaverProposalRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.ProposalTableName);
            entity.HasKey(x => x.ProposalId);
            entity.Property(x => x.ProposalId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Payload).HasColumnType(payloadType).IsRequired();
            entity.HasIndex(x => new { x.SessionId, x.Sequence });
            entity.HasOne<WeaverSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WeaverCallKeyRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.CallKeyTableName);
            entity.HasKey(x => new { x.SessionId, x.Kind, x.KeyHash });
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(KindLength).IsRequired();
            entity.Property(x => x.KeyHash).HasMaxLength(HashLength).IsRequired();
            entity.Property(x => x.Key).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.HasOne<WeaverSessionRow>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WeaverAuditRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.AuditTableName);
            entity.HasKey(x => x.Sequence);
            // Allocated by the store from the audit sequence row; no provider identity column is used.
            entity.Property(x => x.Sequence).ValueGeneratedNever();
            entity.Property(x => x.EventId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength);
            entity.Property(x => x.ActorId).HasMaxLength(PrincipalLength);
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
            entity.Property(x => x.Payload).HasColumnType(payloadType).IsRequired();
            entity.HasIndex(x => new { x.SessionId, x.CreatedAtUtcTicks });
            entity.HasIndex(x => x.CreatedAtUtcTicks);
        });
        modelBuilder.Entity<WeaverAuditSequenceRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.AuditSequenceTableName);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
        });
        modelBuilder.Entity<WeaverAuditOwnerRow>(entity =>
        {
            entity.ToTable(WeaverEfModule.AuditOwnerTableName);
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasMaxLength(IdentityLength).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(PrincipalLength).IsRequired();
            entity.Property(x => x.TenantId).HasMaxLength(PrincipalLength).IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(SchemaVersionLength).IsRequired();
        });
        // Owner, workspace and identity columns compare and order ordinally, as the default store's StringComparer.Ordinal does.
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, Entities,
            nameof(WeaverSessionRow.Workspace), nameof(WeaverSessionRow.OwnerToken), nameof(WeaverSessionRow.SchemaVersion), nameof(WeaverAuditRow.ActorId), nameof(WeaverAuditOwnerRow.TenantId));
        return modelBuilder;
    }
}
