using System.Reflection;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Workflows;
using static MachineInboxHarness;

// Direct model/service boundary fixtures, not durable actor/provider executions.
internal static class ClrBoundaryReaders
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in new[] { "cancel", "owner", "diagnostic" })
        {
            var registry = new WorkflowRegistry(); var node = Wf.Node<ClrBoundaryLeaf>("boundary", ("Kind", Binding.Literal(kind)));
            var wf = Wf.Workflow("boundary", node); wf.InterventionRequested = true; registry.Add(wf);
            var proxy = DispatchProxy.Create<ITurn, ClrBoundaryTurn>(); var setup = (ClrBoundaryTurn)(object)proxy;
            setup.Native = new NativeState { Mode = mode }; var token = new CancellationToken(kind == "cancel"); var observed = false;
            try { new NativeModel(registry).TurnAsync(proxy, token).AsTask().GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (kind == "cancel") { observed = true; }
            catch (InvalidOperationException e) when (kind == "owner" && e.Message == "owner boundary sentinel") { observed = true; }
            if (kind == "diagnostic") observed = setup.Native.Frames.Values.SingleOrDefault()?.Intervention is { Fault.Code: "exception", Fault.Message.Length: 2048 };
            Check(observed && (kind == "diagnostic" || setup.Native.Frames.Values.All(f => f.Intervention is null)), "X20: runtime cancellation/owner failure was captured as an authored incident: " + kind);
            Console.WriteLine($"PASS clr-model-boundary-fixture:{mode},kind={kind}");
        }
        foreach (var policy in Enumerable.Range(0, 5))
        {
            var state = new NativeState { Started = true, InterventionEnabled = true, InterventionPolicy = policy }; var before = Json.Write(state);
            Check(new NativeModel(new()).UpgradeState(before, 10) is NativeState upgraded && Json.Write(upgraded) == before, "X22: old policy gained authored CLR interception");
            Console.WriteLine("PASS clr-reader-v10-policy:" + policy);
        }
        var mixed = new NativeState { Started = true, InterventionEnabled = true, InterventionPolicy = 5 }; var refused = false;
        try { new NativeModel(new()).UpgradeState(Json.Write(mixed), 10); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "X22: old version carried the new CLR policy"); Console.WriteLine("PASS clr-reader-mixed-policy5-v10");
    }
}
internal sealed class ClrBoundaryLeaf : Kernel.Native.Activity
{
    public ClrBoundaryLeaf() { }
    [Input] public string Kind { get; set; } = "";
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c) => Kind switch
    {
        "cancel" => throw new OperationCanceledException(c.CancellationToken),
        "diagnostic" => throw new InvalidOperationException(new string('x', 4096)),
        _ => new(Complete().With(new EmitEffect("owner", null)))
    };
}
// DispatchProxy requires an inheritable accessible proxy type; this is test-only and nonpackable.
public class ClrBoundaryTurn : DispatchProxy
{
    internal NativeState Native = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_State" => Native,
        "set_State" => Set((NativeState)args![0]!),
        "get_Instance" => new InstanceInfo(InstanceRef.In(0, "boundary"), "native", "boundary", null, 1),
        "get_Inbox" => new Message[] { new("start", InstanceRef.In(0, "boundary"), Kinds.Start, Start: NativeModel.Spec("boundary", Native.Mode)) },
        "get_Ambient" => null,
        "get_Now" => 0L,
        "Record" => null,
        "Emit" => throw new InvalidOperationException("owner boundary sentinel"),
        _ => throw new InvalidOperationException("Unexpected boundary fixture call: " + method.Name)
    };
    private object? Set(NativeState state) { Native = state; return null; }
}
