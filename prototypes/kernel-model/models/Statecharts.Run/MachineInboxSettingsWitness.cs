using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using Kernel.Workflows;
using static MachineInboxHarness;

internal static class MachineInboxSettingsWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            ChartDependency.Calls.Clear(); var h = new MachineInboxHarness();
            var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Entry: "phase"), new("B", NodeKind.Final, "root")],
                [new("go", 0, "A", "B", VisitMode.Reenter, "go")]);
            var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)), ("InputInbox", Binding.Var("settings")))
                .With(Wf.Sequence("phase", Wf.Set("change", "settings", "incompatible-after-entry"), Wf.Node<ChartDependency>("dependency", ("Local", Binding.Literal(17)))));
            var wf = new Workflow("machine-inbox", machine, new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["settings"] = Values.From(new { Capacity = 2, ReceiptCapacity = 8, RetentionMilliseconds = 120000L }) }); wf.InterventionRequested = true;
            Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
            var row = h.World.Store.Instances[h.Target.Id];
            var previous = new WorkflowInstance([new VersionNineModel()]);
            Check(row.StateVersion == 10 && previous.Withholds(row, new GateServices()) && !previous.Withholds(row with { StateVersion = 9 }, new GateServices()),
                "Q18: version9 host would run the new machine checkpoint");
            Console.WriteLine("PASS machine-inbox-v9-capability-gate:" + mode + " (capability facade; not a historical binary)");
            var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
            Check(h.State.Variables["settings"]?.ToString() == "incompatible-after-entry", "Q17: settings fixture did not change its ordinary bound value");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
            Check(h.State.Inboxes![frame].Capacity == 2 && h.State.Inboxes[frame].Receipts["go"].Status == "Queued", "Q17: pending receiver reevaluated frozen entry settings");
            for (var i = 0; i < 2; i++)
            {
                var source = h.State.Frames.Values.Single(f => f.Node == "dependency");
                var request = h.Send(NativeRetry.Kind, new NativeRetry("fix-" + i, source.Id, source.Intervention!.Id, "frozen settings")); h.Drain(request); h.Reload();
            }
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.Inboxes![frame].Receipts["go"].Outcome == "handled", "Q17: ordinary callback reevaluated frozen settings");
            Console.WriteLine("PASS machine-inbox-frozen-expression:" + mode);
        }
    }
}

// Exercise the unchanged shared future-state gate with an explicit previous-host capability.
internal sealed class VersionNineModel : IExecutionModel
{
    public string Key => NativeModel.ModelKey;
    public int StateVersion => 9;
    public Type StateType => typeof(NativeState);
    public object CreateState(StartSpec s) => throw new InvalidOperationException("Q18: old model was invoked");
    public object UpgradeState(string s, int v) => throw new InvalidOperationException("Q18: old model was invoked");
    public ValueTask TurnAsync(ITurn t, CancellationToken c) => throw new InvalidOperationException("Q18: old model was invoked");
    public ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken c) => throw new InvalidOperationException("Q18: old model was invoked");
}
internal sealed class GateServices : ITurnServices
{
    public long Now => 0;
    public bool CommitClockAvailable => true;
    public bool CreationIdentityAvailable => true;
    public int Partitions => 1;
    public TimeSpan ShortWait => TimeSpan.Zero;
    public bool Has(Flaw f) => false;
    public void Cancelling(InboxKey k) => throw new InvalidOperationException("Q18: gate cancelled work");
}
