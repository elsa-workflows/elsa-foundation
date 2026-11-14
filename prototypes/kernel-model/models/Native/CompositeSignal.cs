namespace Kernel.Native;

/// <summary>The prototype addresses a live invocation by frame, including when the same node runs twice.</summary>
public sealed record CompositeSignal(string Frame, string Name, string? Value = null)
{
    public const string Kind = "native.composite-signal";
}

public interface ISignalledComposite
{
    bool CanAccept(CompositeContext context);
    void Accept(CompositeContext context, string messageId, CompositeSignal signal);
}

internal sealed partial class Burst
{
    private List<(Message Message, CompositeSignal Signal)>? _deferredSignals;
    private HashSet<string>? _signalledFrames;

    private (ISignalledComposite Receiver, CompositeContext Context)? Receiver(string frameId)
    {
        if (!_s.Frames.TryGetValue(frameId, out var frame) || frame.Intervention is not null) return null;
        var node = _wf.Nodes[frame.Node];
        if (!typeof(ISignalledComposite).IsAssignableFrom(node.Type)) return null;
        var receiver = (ISignalledComposite)Hydrator.Create(node, b => Evaluate(b, new FrameScope(this, frame)));
        return (receiver, new CompositeContext(this, frame, node));
    }

    private void AcceptSignal(Message message)
    {
        var signal = message.Read<CompositeSignal>();
        var target = Receiver(signal.Frame);
        if (target is null)
        {
            turn.Record($"signal-rejected:{message.Id}:{signal.Frame}");
            return;
        }
        var (receiver, context) = target.Value;
        _signalledFrames ??= [];
        if (!_signalledFrames.Contains(signal.Frame) && receiver.CanAccept(context))
        {
            _signalledFrames.Add(signal.Frame);
            receiver.Accept(context, message.Id, signal);
            return;
        }
        (_deferredSignals ??= []).Add((message, signal));
    }

    private void FinishSignals()
    {
        if (_deferredSignals is null) return;
        var ready = false;
        foreach (var (message, signal) in _deferredSignals)
        {
            var target = Receiver(signal.Frame);
            if (target is null)
            {
                turn.Record($"signal-rejected:{message.Id}:{signal.Frame}");
                continue;
            }
            turn.Hold(message);
            turn.Record($"signal-deferred:{message.Id}:{signal.Frame}");
            ready |= target.Value.Receiver.CanAccept(target.Value.Context);
        }
        // A held inbox alone sleeps. If this turn finished an action or accepted an earlier signal,
        // wake the ready receiver in the next turn. A still-suspended action supplies its own wake.
        if (ready) turn.Send(turn.Instance.Ref, NativeKinds.Continue);
    }
}
