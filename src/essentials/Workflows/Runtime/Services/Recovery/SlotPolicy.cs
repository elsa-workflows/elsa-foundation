namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>
/// The share each side of a capped resumption sweep gets, in one place (#2188). The recovery scanner keeps half the cap
/// (at least one slot, at most its batch size) and the backlog the rest, and either side may use what the other leaves.
/// A cap of one cannot be halved, so its single slot alternates: the side that used it hands the turn to the other.
/// </summary>
internal sealed record SlotPolicy(int? Max, int ScanLimit, bool RecoveryHasSingleSlotTurn)
{
    public int Capacity => Max ?? int.MaxValue;

    private int RecoveryShare => Max switch
    {
        null => ScanLimit,
        1 => RecoveryHasSingleSlotTurn ? 1 : 0,
        { } max => Math.Min(ScanLimit, Math.Max(1, max / 2))
    };

    // Backlog demand beyond the backlog's own share leaves the scanner its share all the same, so counting further
    // would only check executions the sweep may not use.
    public int BacklogDemandWorthCounting => Max is { } max ? max - RecoveryShare : 0;

    public int RecoveryLimit(int backlogDemand) =>
        Max is { } max ? Math.Min(ScanLimit, max - Math.Min(backlogDemand, max - RecoveryShare)) : ScanLimit;

    public bool NextRecoveryTurn(bool recoveryUsedSlot, bool slotUsed) =>
        Max == 1 && slotUsed ? !recoveryUsedSlot : RecoveryHasSingleSlotTurn;
}
