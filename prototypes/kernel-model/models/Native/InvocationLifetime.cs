namespace Kernel.Native;

internal static class InvocationLifetime
{
    internal static InputReceipt Final(InputReceipt receipt, string status, string outcome, long now) =>
        receipt with { Status = status, Outcome = outcome, FinalizedAt = now,
            ValidUntil = receipt.Retention is { } duration ? checked(now + duration) : null };

    internal static void Close(NativeState state, string frame, string reason, long now, bool keepPayload = false)
    {
        if (state.Inboxes?.GetValueOrDefault(frame) is not { Closed: null } inbox) return;
        inbox.Closed = reason;
        foreach (var (id, receipt) in inbox.Receipts.ToArray())
            if (receipt.Status is "Queued" or "Selected")
                inbox.Receipts[id] = Final(receipt, receipt.Status == "Selected" ? "Interrupted" : "NotProcessed", reason, now);
        if (!keepPayload) inbox.Pending.Clear(); // only the receipt-owned payload area; ordinary activity state is owned elsewhere
    }

    internal static void Terminate(NativeState state, long now, bool keepPayload = false)
    {
        foreach (var frame in state.Inboxes?.Keys.ToArray() ?? []) Close(state, frame, "cancelled", now, keepPayload);
    }

    internal static bool Protected(NativeState state, long now) => state.Inboxes?.Values.Any(inbox =>
        inbox.Receipts.Values.Any(r => r.ValidUntil is null || now < r.ValidUntil)) == true;

    internal static void Clean(InvocationInbox inbox, long now)
    {
        foreach (var (id, receipt) in inbox.Receipts.ToArray())
            if (receipt.ValidUntil is { } until && now >= until) inbox.Receipts.Remove(id);
    }
}
