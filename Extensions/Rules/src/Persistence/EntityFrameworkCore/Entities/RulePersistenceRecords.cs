namespace Elsa.Rules.Persistence.EntityFrameworkCore.Entities;

/// <summary>Current semantic state and independent worker/delivery lease projections for one Rules invocation.</summary>
internal sealed class RuleRunRecord
{
    public string RunKey { get; set; } = null!;
    public string IdentityBase64 { get; set; } = null!;
    public string InvocationJson { get; set; } = null!;
    public string Fingerprint64 { get; set; } = null!;
    public string PinHash64 { get; set; } = null!;
    public bool Admitted { get; set; }
    public long ModelRevision { get; set; }
    public byte[] ConcurrencyToken { get; set; } = [];
    public string FactsJson { get; set; } = null!;
    public string FactsSha25664 { get; set; } = null!;
    public int CommittedRounds { get; set; }
    public string? PendingReceiptId64 { get; set; }
    public int Status { get; set; }
    public string? TerminalId64 { get; set; }
    public string? TerminalJson { get; set; }
    public long? TerminalModelRevision { get; set; }
    public string? WorkOwner { get; set; }
    public long WorkFence { get; set; }
    public long? WorkExpiresAtUtcTicks { get; set; }
    public string? DeliveryId64 { get; set; }
    public string? DeliveryPayloadJson { get; set; }
    public string? DeliveryPayloadSha25664 { get; set; }
    public string? DeliveryOwner { get; set; }
    public long DeliveryFence { get; set; }
    public long? DeliveryExpiresAtUtcTicks { get; set; }
    public bool DeliveryAcknowledged { get; set; }
    public string? DeliveryAcknowledgementJson { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

/// <summary>Immutable logical round receipt with first accepted physical attempt and one-time consumption proof.</summary>
internal sealed class RuleRoundReceiptRecord
{
    public string RunKey { get; set; } = null!;
    public int Ordinal { get; set; }
    public string ReceiptId64 { get; set; } = null!;
    public string InputFactsSha25664 { get; set; } = null!;
    public string ReceiptJson { get; set; } = null!;
    public string AcceptedAttemptId { get; set; } = null!;
    public string AcceptedWorkerId { get; set; } = null!;
    public long AcceptedFence { get; set; }
    public long AcceptedModelRevision { get; set; }
    public long? ConsumedModelRevision { get; set; }
    public string? ConsumptionTransitionId64 { get; set; }
    public string? TerminalIdAtConsumption64 { get; set; }
    public string? NextRoundIdAtConsumption64 { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

/// <summary>Append-only first-visibility history; atom text is never a database key or collation predicate.</summary>
internal sealed class RuleFactHistoryRecord
{
    public string RunKey { get; set; } = null!;
    public int FactIndex { get; set; }
    public string Atom { get; set; } = null!;
    public long FirstVisibleModelRevision { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
