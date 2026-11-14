namespace Kernel.Native;

// Private composition seam: the existing native inbox remains the sole receipt/selection owner.
// Post-entry callbacks use context/checkpoint material, without re-binding entry-only inputs.
internal interface IInvocationInputReceiver
{
    bool CanAcceptInput(CompositeContext context);
    void AcceptInput(CompositeContext context, InvocationInput input);
}

internal sealed partial class Burst
{
    private ActivityBase RestoreActivity(Frame frame, Node node) => typeof(IInvocationInputReceiver).IsAssignableFrom(node.Type)
        ? (ActivityBase)Activator.CreateInstance(node.Type)!
        : Hydrator.Create(node, b => Evaluate(b, new FrameScope(this, frame)));

    private void DrainInputs()
    {
        foreach (var id in _s.Inboxes?.Keys.ToArray() ?? [])
        {
            if (!_s.Frames.TryGetValue(id, out var frame) || Blocked(id) ||
                !_s.Inboxes![id].Pending.Any(i => _s.Inboxes[id].Receipts[i.Id].Status == "Queued")) continue;
            var node = _wf.Nodes[frame.Node];
            if (!typeof(IInvocationInputReceiver).IsAssignableFrom(node.Type)) continue;
            var receiver = (IInvocationInputReceiver)RestoreActivity(frame, node);
            var context = new CompositeContext(this, frame, node);
            if (!receiver.CanAcceptInput(context)) continue;
            receiver.AcceptInput(context, SelectInput(frame)!);
            // Selection may schedule ordinary work, or settle immediately with another queued input.
            // Persist one wake; never inline another business input in this turn.
            turn.Send(turn.Instance.Ref, NativeKinds.Continue);
        }
    }

    internal void ValidateInputInbox(Frame frame, int capacity, int receiptCapacity, long retention, string? selected)
        => InvocationInputs.Validate(_s.Inboxes?.GetValueOrDefault(frame.Id), frame.Id, capacity, receiptCapacity, retention, selected);
}

internal static class InvocationInputs
{
    internal static void Validate(InvocationInbox? inbox, string frame, int capacity, int receiptCapacity, long retention, string? selected)
    {
        if (inbox is not { Closed: null } ||
            inbox.Capacity != capacity || inbox.ReceiptCapacity != receiptCapacity || inbox.Retention != retention ||
            !inbox.Receipts.Where(r => r.Value.Status == "Selected").Select(r => r.Key).Order(StringComparer.Ordinal)
                .SequenceEqual(selected is null ? [] : new[] { selected }) ||
            selected is not null && inbox.Pending.Count(i => i.Frame == frame && i.Id == selected) != 1)
            throw new InvalidOperationException("Statechart input inbox does not match its frozen selection.");
    }
}

public sealed partial class CompositeContext
{
    internal void ValidateInputInbox(int capacity, int receiptCapacity, long retention, string? selected) =>
        _burst.ValidateInputInbox(_frame, capacity, receiptCapacity, retention, selected);
}
