namespace Kernel.Review5;

/// <summary>
/// A harness-authored composite activity, copied from Review4.Blockers (Fr1.cs): it defers every signal until its child
/// completes, as Incarnation.Run's HeldReceiver does. It runs inside the real NativeModel, whose real signal path
/// (CompositeSignal.cs, FinishSignals) holds the deferred signal with ITurn.Hold.
/// </summary>
public sealed class HeldGate : CompositeActivity, ISignalledComposite
{
    public static int Accepts;
    protected override void Execute(CompositeContext context) { context.SetState(false); context.Schedule(context.Children.Single()); }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.SetState(true);
    public bool CanAccept(CompositeContext context) => context.GetState<bool>();
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal) { Accepts++; context.Complete(); }
}

/// <summary>
/// A background leaf that reports what it received, after Astra4.Sizes' EffectEcho: the activity code of a Background
/// node, run by the real <c>NativeModel.ExecuteJobAsync</c>. Each entry is a physical invocation and is counted.
/// </summary>
[Background]
public sealed class EffectEcho : Activity<string>
{
    public static int Invocations;
    [Input] public string Service { get; set; } = "";
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Invocations++;
        return new(Complete($"{Service}|{context.EffectKey}"));
    }
}
