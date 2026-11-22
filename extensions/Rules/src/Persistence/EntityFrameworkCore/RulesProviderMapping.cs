using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Provider-specific column storage shared by the relational Rules contexts.</summary>
internal static class RulesProviderMapping
{
    private static readonly string[] OrdinallyComparedColumns =
    [
        nameof(RuleRunRecord.IdentityBase64),
        nameof(RuleRunRecord.Fingerprint64),
        nameof(RuleRunRecord.PinHash64),
        nameof(RuleRunRecord.FactsSha25664),
        nameof(RuleRunRecord.PendingReceiptId64),
        nameof(RuleRunRecord.TerminalId64),
        nameof(RuleRunRecord.WorkOwner),
        nameof(RuleRunRecord.DeliveryId64),
        nameof(RuleRunRecord.DeliveryPayloadSha25664),
        nameof(RuleRunRecord.DeliveryOwner),
        nameof(RuleRoundReceiptRecord.ReceiptId64),
        nameof(RuleRoundReceiptRecord.InputFactsSha25664),
        nameof(RuleRoundReceiptRecord.AcceptedAttemptId),
        nameof(RuleRoundReceiptRecord.AcceptedWorkerId),
        nameof(RuleRoundReceiptRecord.ConsumptionTransitionId64),
        nameof(RuleRoundReceiptRecord.TerminalIdAtConsumption64),
        nameof(RuleRoundReceiptRecord.NextRoundIdAtConsumption64),
        nameof(RuleFactHistoryRecord.Atom),
        nameof(RuleFactHistoryRecord.SchemaVersion)
    ];

    internal static void Configure(ModelBuilder modelBuilder, string providerName, string largeTextType, string tokenType)
    {
        modelBuilder.Entity<RuleRunRecord>(entity =>
        {
            entity.Property(row => row.IdentityBase64).HasColumnType(largeTextType);
            entity.Property(row => row.InvocationJson).HasColumnType(largeTextType);
            entity.Property(row => row.ConcurrencyToken).HasColumnType(tokenType);
            entity.Property(row => row.FactsJson).HasColumnType(largeTextType);
            entity.Property(row => row.TerminalJson).HasColumnType(largeTextType);
            entity.Property(row => row.DeliveryPayloadJson).HasColumnType(largeTextType);
            entity.Property(row => row.DeliveryAcknowledgementJson).HasColumnType(largeTextType);
        });

        modelBuilder.Entity<RuleRoundReceiptRecord>(entity =>
            entity.Property(row => row.ReceiptJson).HasColumnType(largeTextType));

        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);
    }
}
