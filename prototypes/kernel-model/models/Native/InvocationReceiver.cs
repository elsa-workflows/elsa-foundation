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
        if (_checkpointRequested) return;
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
            if (_checkpointRequested) return;
            // Selection may schedule ordinary work, or settle immediately with another queued input.
            // Persist one wake; never inline another business input in this turn.
            turn.Send(turn.Instance.Ref, NativeKinds.Continue);
        }
    }

    internal void ValidateInputInbox(Frame frame, int capacity, int receiptCapacity, long retention, string? selected, bool evaluating = false)
        => InvocationInputs.Validate(_s.Inboxes?.GetValueOrDefault(frame.Id), frame.Id, capacity, receiptCapacity, retention, selected, evaluating);

    internal InvocationInput ReadActiveInput(Frame frame, string id, bool evaluating) =>
        InvocationInputs.Read(_s.Inboxes?.GetValueOrDefault(frame.Id), frame.Id, id, evaluating);

    internal void BeginEvaluation(Frame frame, string id)
    {
        _ = ReadActiveInput(frame, id, false);
        var inbox = _s.Inboxes![frame.Id];
        if (inbox.Receipts[id].EvaluationStarted) throw new InvalidOperationException("Input evaluation already started.");
        inbox.Receipts[id] = inbox.Receipts[id] with { Status = "Evaluating", EvaluationStarted = true };
    }

    internal void SelectEvaluation(Frame frame, string id)
    {
        _ = ReadActiveInput(frame, id, true);
        var inbox = _s.Inboxes![frame.Id];
        inbox.Receipts[id] = inbox.Receipts[id] with { Status = "Selected" };
    }
}

internal static class InvocationInputs
{
    internal static void Validate(InvocationInbox? inbox, string frame, int capacity, int receiptCapacity, long retention, string? selected, bool evaluating = false)
    {
        if (inbox is not { Closed: null } ||
            inbox.Capacity != capacity || inbox.ReceiptCapacity != receiptCapacity || inbox.Retention != retention ||
            !inbox.Receipts.Where(r => r.Value.Status is "Selected" or "Evaluating").Select(r => r.Key).Order(StringComparer.Ordinal)
                .SequenceEqual(selected is null ? [] : new[] { selected }) ||
            selected is not null && inbox.Pending.Count(i => i.Frame == frame && i.Id == selected) != 1 ||
            selected is not null && (inbox.Receipts[selected].Status != (evaluating ? "Evaluating" : "Selected") || evaluating && !inbox.Receipts[selected].EvaluationStarted))
            throw new InvalidOperationException("Statechart input inbox does not match its frozen selection.");
    }

    internal static InvocationInput Read(InvocationInbox? inbox, string frame, string id, bool evaluating)
    {
        if (inbox is null) throw new InvalidOperationException("Missing active input inbox.");
        Validate(inbox, frame, inbox.Capacity, inbox.ReceiptCapacity, inbox.Retention ?? 0, id, evaluating);
        if (inbox.Pending.Count(i => i.Id == id) != 1) throw new InvalidOperationException("Ambiguous active input.");
        var input = inbox.Pending.Single(i => i.Id == id);
        if (input.Frame != frame || Burst.Digest(input) != inbox.Receipts[id].Digest)
            throw new InvalidOperationException("Active input differs from its native receipt.");
        return input;
    }
}

public sealed partial class CompositeContext
{
    internal void ValidateInputInbox(int capacity, int receiptCapacity, long retention, string? selected, bool evaluating = false) =>
        _burst.ValidateInputInbox(_frame, capacity, receiptCapacity, retention, selected, evaluating);
    internal InvocationInput ReadActiveInput(string id, bool evaluating) => _burst.ReadActiveInput(_frame, id, evaluating);
    internal void BeginInputEvaluation(string id) => _burst.BeginEvaluation(_frame, id);
    internal void SelectInputEvaluation(string id) => _burst.SelectEvaluation(_frame, id);
    internal void FinishInputEvaluation(string id) => _burst.FinishInput(_frame, id, "unhandled", evaluation: true);
}
