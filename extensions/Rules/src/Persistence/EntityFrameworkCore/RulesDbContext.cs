using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral EF model for the Rules-owned durable state and its immutable history.</summary>
internal abstract class RulesDbContext : DbContext
{
    protected RulesDbContext(DbContextOptions options) : base(options)
    {
    }

    internal DbSet<RuleRunRecord> Runs => Set<RuleRunRecord>();
    internal DbSet<RuleRoundReceiptRecord> Receipts => Set<RuleRoundReceiptRecord>();
    internal DbSet<RuleFactHistoryRecord> Facts => Set<RuleFactHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);

        var runs = modelBuilder.Entity<RuleRunRecord>();
        runs.ToTable(RulesEfModule.RunTableName);
        runs.HasKey(row => row.RunKey);
        runs.Property(row => row.RunKey).HasMaxLength(RulesEfModule.HashLength).IsRequired();
        runs.Property(row => row.IdentityBase64).IsRequired();
        runs.Property(row => row.InvocationJson).IsRequired();
        ConfigureHash(runs.Property(row => row.Fingerprint64));
        ConfigureHash(runs.Property(row => row.PinHash64));
        runs.Property(row => row.Admitted).IsRequired();
        runs.Property(row => row.ModelRevision).IsRequired();
        runs.Property(row => row.ConcurrencyToken).HasMaxLength(16).IsConcurrencyToken().ValueGeneratedNever().IsRequired();
        runs.Property(row => row.FactsJson).IsRequired();
        ConfigureHash(runs.Property(row => row.FactsSha25664));
        runs.Property(row => row.CommittedRounds).IsRequired();
        ConfigureOptionalHash(runs.Property(row => row.PendingReceiptId64));
        runs.Property(row => row.Status).IsRequired();
        ConfigureOptionalHash(runs.Property(row => row.TerminalId64));
        runs.Property(row => row.TerminalJson);
        runs.Property(row => row.TerminalModelRevision);
        ConfigureOptionalIdentifier(runs.Property(row => row.WorkOwner));
        runs.Property(row => row.WorkFence).IsRequired();
        runs.Property(row => row.WorkExpiresAtUtcTicks);
        ConfigureOptionalHash(runs.Property(row => row.DeliveryId64));
        runs.Property(row => row.DeliveryPayloadJson);
        ConfigureOptionalHash(runs.Property(row => row.DeliveryPayloadSha25664));
        ConfigureOptionalIdentifier(runs.Property(row => row.DeliveryOwner));
        runs.Property(row => row.DeliveryFence).IsRequired();
        runs.Property(row => row.DeliveryExpiresAtUtcTicks);
        runs.Property(row => row.DeliveryAcknowledged).IsRequired();
        runs.Property(row => row.DeliveryAcknowledgementJson);
        ConfigureSchemaVersion(runs.Property(row => row.SchemaVersion));

        var receipts = modelBuilder.Entity<RuleRoundReceiptRecord>();
        receipts.ToTable(RulesEfModule.ReceiptTableName);
        receipts.HasKey(row => new { row.RunKey, row.Ordinal });
        receipts.Property(row => row.RunKey).HasMaxLength(RulesEfModule.HashLength).IsRequired();
        receipts.Property(row => row.Ordinal).IsRequired();
        ConfigureHash(receipts.Property(row => row.ReceiptId64));
        ConfigureHash(receipts.Property(row => row.InputFactsSha25664));
        receipts.Property(row => row.ReceiptJson).IsRequired();
        ConfigureIdentifier(receipts.Property(row => row.AcceptedAttemptId));
        ConfigureIdentifier(receipts.Property(row => row.AcceptedWorkerId));
        receipts.Property(row => row.AcceptedFence).IsRequired();
        receipts.Property(row => row.AcceptedModelRevision).IsRequired();
        receipts.Property(row => row.ConsumedModelRevision);
        ConfigureOptionalHash(receipts.Property(row => row.ConsumptionTransitionId64));
        ConfigureOptionalHash(receipts.Property(row => row.TerminalIdAtConsumption64));
        ConfigureOptionalHash(receipts.Property(row => row.NextRoundIdAtConsumption64));
        ConfigureSchemaVersion(receipts.Property(row => row.SchemaVersion));
        receipts.HasIndex(row => row.ReceiptId64).IsUnique();
        receipts.HasOne<RuleRunRecord>().WithMany().HasForeignKey(row => row.RunKey).OnDelete(DeleteBehavior.Restrict);

        var facts = modelBuilder.Entity<RuleFactHistoryRecord>();
        facts.ToTable(RulesEfModule.FactTableName);
        facts.HasKey(row => new { row.RunKey, row.FactIndex });
        facts.Property(row => row.RunKey).HasMaxLength(RulesEfModule.HashLength).IsRequired();
        facts.Property(row => row.FactIndex).IsRequired();
        facts.Property(row => row.Atom).HasMaxLength(RulesEfModule.IdentifierMaximumLength).IsRequired();
        facts.Property(row => row.FirstVisibleModelRevision).IsRequired();
        ConfigureSchemaVersion(facts.Property(row => row.SchemaVersion));
        facts.HasOne<RuleRunRecord>().WithMany().HasForeignKey(row => row.RunKey).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.MapSchemaFinalization(RulesEfModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
        ConfigureProvider(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    internal abstract Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken);

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    private void StampConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<RuleRunRecord>()
                     .Where(candidate => candidate.State is EntityState.Added or EntityState.Modified))
            entry.Property(row => row.ConcurrencyToken).CurrentValue = Guid.NewGuid().ToByteArray();
    }

    private static void ConfigureHash(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(RulesEfModule.HashLength).IsRequired();

    private static void ConfigureOptionalHash(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string?> property) =>
        property.HasMaxLength(RulesEfModule.HashLength);

    private static void ConfigureIdentifier(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(RulesEfModule.IdentifierMaximumLength).IsRequired();

    private static void ConfigureOptionalIdentifier(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string?> property) =>
        property.HasMaxLength(RulesEfModule.IdentifierMaximumLength);

    private static void ConfigureSchemaVersion(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(RulesEfModule.SchemaVersionMaximumLength).IsRequired();
}
