using System.Text.Json;

namespace Kernel.Native;

internal static class InvocationLifetime
{
    // A failed initial turn can legitimately leave no model state (Park's persisted JSON null).
    internal static ValueTask Missing(ITurn turn)
    {
        foreach (var message in turn.Inbox.Where(m => m.Kind is InvocationInput.Kind or ReceiptQuery.Kind))
        {
            Admission result;
            try
            {
                var address = message.Payload is null ? null : message.Kind == ReceiptQuery.Kind
                    ? message.Read<ReceiptQuery>() : message.Read<InvocationInput>() is { } input ? new ReceiptQuery(input.Frame, input.Id) : null;
                result = address is null ? new("", "", "Invalid", Actor: turn.Instance.Ref.Id)
                    : new(address.Frame, address.Id, "UnknownInvocation", Actor: turn.Instance.Ref.Id);
            }
            catch (JsonException) { result = new("", "", "Invalid", Actor: turn.Instance.Ref.Id); }
            turn.Reply(message, Request.Response, result);
        }
        return ValueTask.CompletedTask;
    }

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
