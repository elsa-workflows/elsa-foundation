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

    internal static InputReceipt Final(InputReceipt receipt, string status, string outcome, string? reference) =>
        receipt with { Status = status, Outcome = outcome, Clock = reference };

    internal static void Close(NativeState state, string frame, string reason, string? reference, bool keepPayload = false, bool restampCompleted = false)
    {
        if (state.Inboxes?.GetValueOrDefault(frame) is not { Closed: null } inbox) return;
        inbox.Closed = reason;
        if (restampCompleted)
            foreach (var (id, receipt) in inbox.Receipts.ToArray())
                if (receipt.Status == "Completed") inbox.Receipts[id] = receipt with { Clock = reference };
        foreach (var (id, receipt) in inbox.Receipts.ToArray())
            if (receipt.Status is "Queued" or "Selected")
                inbox.Receipts[id] = Final(receipt, receipt.Status == "Selected" ? "Interrupted" : "NotProcessed", reason, reference);
        if (!keepPayload) inbox.Pending.Clear();
    }

    internal static void Terminate(NativeState state, string reference, string reason, bool keepPayload = false, bool restampCompleted = false)
    {
        foreach (var frame in state.Inboxes?.Keys.ToArray() ?? []) Close(state, frame, reason, reference, keepPayload, restampCompleted);
    }

    internal static long? Deadline(InputReceipt receipt, ICommitClock clock)
    {
        if (receipt.Clock is not { } reference || receipt.Retention is not { } duration || clock.Resolve(reference) is not { } time)
            return null;
        try { return checked(time + duration); }
        catch (OverflowException) { return null; } // unavailable time cannot authorize expiry
    }

    internal static bool Protected(NativeState state, long now, ICommitClock clock) => state.Inboxes?.Values.Any(inbox =>
        inbox.Receipts.Values.Any(r => Deadline(r, clock) is not { } until || now < until)) == true;

    internal static void Clean(NativeState state, InvocationInbox inbox, long now, ICommitClock clock, bool releaseEarly = false)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, receipt) in inbox.Receipts.ToArray())
            if (Deadline(receipt, clock) is { } until && now >= until)
            {
                inbox.Receipts.Remove(id);
                if (receipt.Clock is { } reference) removed.Add(reference);
            }
        var retained = state.Inboxes!.Values.SelectMany(x => x.Receipts.Values).Select(r => r.Clock).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in removed)
            if (reference.StartsWith(CommitClockFacts.ModelPrefix, StringComparison.Ordinal) && (releaseEarly || !retained.Contains(reference))) clock.Release(reference);
    }
}
