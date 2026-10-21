using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>SQLite mapping for the provider-neutral Rules runtime model.</summary>
internal sealed class RulesSqliteDbContext : RulesDbContext
{
    public RulesSqliteDbContext(DbContextOptions<RulesSqliteDbContext> options) : base(options)
    {
    }

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RuleRunRecord>(entity =>
        {
            entity.Property(row => row.RunKey).HasColumnType("TEXT");
            entity.Property(row => row.IdentityBase64).HasColumnType("TEXT");
            entity.Property(row => row.InvocationJson).HasColumnType("TEXT");
            entity.Property(row => row.Fingerprint64).HasColumnType("TEXT");
            entity.Property(row => row.PinHash64).HasColumnType("TEXT");
            entity.Property(row => row.ConcurrencyToken).HasColumnType("BLOB");
            entity.Property(row => row.FactsJson).HasColumnType("TEXT");
            entity.Property(row => row.FactsSha25664).HasColumnType("TEXT");
            entity.Property(row => row.PendingReceiptId64).HasColumnType("TEXT");
            entity.Property(row => row.TerminalId64).HasColumnType("TEXT");
            entity.Property(row => row.TerminalJson).HasColumnType("TEXT");
            entity.Property(row => row.WorkOwner).HasColumnType("TEXT");
            entity.Property(row => row.DeliveryId64).HasColumnType("TEXT");
            entity.Property(row => row.DeliveryPayloadJson).HasColumnType("TEXT");
            entity.Property(row => row.DeliveryPayloadSha25664).HasColumnType("TEXT");
            entity.Property(row => row.DeliveryOwner).HasColumnType("TEXT");
            entity.Property(row => row.DeliveryAcknowledgementJson).HasColumnType("TEXT");
            entity.Property(row => row.SchemaVersion).HasColumnType("TEXT");
        });

        modelBuilder.Entity<RuleRoundReceiptRecord>(entity =>
        {
            entity.Property(row => row.RunKey).HasColumnType("TEXT");
            entity.Property(row => row.ReceiptId64).HasColumnType("TEXT");
            entity.Property(row => row.InputFactsSha25664).HasColumnType("TEXT");
            entity.Property(row => row.ReceiptJson).HasColumnType("TEXT");
            entity.Property(row => row.AcceptedAttemptId).HasColumnType("TEXT");
            entity.Property(row => row.AcceptedWorkerId).HasColumnType("TEXT");
            entity.Property(row => row.ConsumptionTransitionId64).HasColumnType("TEXT");
            entity.Property(row => row.TerminalIdAtConsumption64).HasColumnType("TEXT");
            entity.Property(row => row.NextRoundIdAtConsumption64).HasColumnType("TEXT");
            entity.Property(row => row.SchemaVersion).HasColumnType("TEXT");
        });

        modelBuilder.Entity<RuleFactHistoryRecord>(entity =>
        {
            entity.Property(row => row.RunKey).HasColumnType("TEXT");
            entity.Property(row => row.Atom).HasColumnType("TEXT");
            entity.Property(row => row.SchemaVersion).HasColumnType("TEXT");
        });
    }
}
