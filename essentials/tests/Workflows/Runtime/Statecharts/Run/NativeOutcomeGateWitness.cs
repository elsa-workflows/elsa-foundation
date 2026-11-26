using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Workflows;
using static NativeInternalWitness;

// Real worker/store handover with a previous capability facade, not a historical host binary.
internal static class NativeOutcomeGateWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } select "native-outcome-old12-worker:" + m;
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) Run(mode).GetAwaiter().GetResult();
    }
    private static async Task Run(string mode)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness(); var wf = Wf.Workflow("machine-inbox", NativeOutcomeWitness.Machine());
        wf.InterventionRequested = true; using var registration = Register(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "held", NativeOutcomeWitness.Finals[0])); h.Drain();
        var wait = h.World.Store.Subscriptions.Single(s => s.Instance == h.Target.Id && s.Key == "event:initial-entry");
        var delivery = Events.Route(new(wait.Key, "old-owner-release", null, h.World.Time + 120000), wait);
        Check(h.World.Store.AddEventAsync(delivery).Result.Added, "NOCG0: old-owner fixture missed its real wait");
        var row = h.World.Store.Instances[h.Target.Id]; var subscriptions = h.World.Store.Subscriptions.ToHashSet();
        Check(row.StateVersion == 13 && h.State.Inboxes![frame].Receipts["held"].Status == "Queued", "NOCG1: mapping checkpoint lacks its outer native13 gate");
        h.Apply(new("crash", 0)); h.Apply(new("tick", 20000));
        var capability = new PreviousTwelve();
        await using var old = new Worker("outcome-old12", 0, h.World.Store, new WorkflowInstance([capability]),
            new WorkerOptions { Monotonic = () => h.World.Time, Lease = TimeSpan.FromSeconds(10) });
        await new DurableHost([old]).ActivateAsync(0);
        for (var i = 0; i < 5; i++) await Pump();
        Check(h.World.Store.Instances[h.Target.Id] == row && subscriptions.SetEquals(h.World.Store.Subscriptions) &&
            h.World.Store.Messages.TryGetValue(delivery.Message.InboxKey, out var pending) && !pending.Done && ChartMark.Calls.Count == 0 && capability.Calls == 0,
            "NOCG2: old12 worker rewrote/parked the mapped row, consumed its wait or invoked model work");
        // Ordinary future-state control is allowed to end the actor without parsing the model's private JSON.
        await h.World.Store.AddAsync(new("old12-terminate", h.Target, Kinds.Terminate, TargetCreation: 1));
        for (var i = 0; i < 5; i++) await Pump();
        var ended = h.World.Store.Instances[h.Target.Id];
        Check(ended.Status == InstanceStatus.Cancelled && ended.State == row.State && ended.StateVersion == 13 &&
            !h.World.Store.Subscriptions.Any(s => s.Instance == h.Target.Id) && capability.Calls == 0,
            "NOCG3: future termination parsed or rewrote private outcome state");
        await h.World.Store.AddAsync(new("old12-query", h.Target, ReceiptQuery.Kind, Json.Write(new ReceiptQuery(frame, "held")), TargetCreation: 1));
        for (var i = 0; i < 5; i++) await Pump();
        Check(h.World.Store.Instances[h.Target.Id].State == row.State && capability.Calls == 0 &&
            h.World.Store.Journal.Values.Where(j => j.Instance == h.Target.Id)
                .SelectMany(j => System.Text.Json.Nodes.JsonNode.Parse(j.Entries)!.AsArray())
                .Any(e => e is System.Text.Json.Nodes.JsonObject o && o["StoredVersion"]?.ToString() == "13" && o["AvailableVersion"]?.ToString() == "12"),
            "NOCG4: ended future metadata did not record capability-unavailable with opaque private state");
        Console.WriteLine("PASS native-outcome-old12-worker:" + mode);
        async Task Pump()
        {
            await old.PollAsync(); if (old.NeedsLoad) await old.LoadAsync();
            await old.RunAsync(); if (old.NeedsFlush) await old.FlushAsync();
        }
    }
    private sealed class PreviousTwelve : IExecutionModel, IModelMetadata
    {
        internal int Calls;
        private Exception Invoked() { Calls++; return new InvalidOperationException("NOCG5: old model was invoked"); }
        public string Key => NativeModel.ModelKey;
        public int StateVersion => 12;
        public Type StateType => typeof(NativeState);
        public object CreateState(StartSpec s) => throw Invoked();
        public object UpgradeState(string s, int v) => throw Invoked();
        public ValueTask TurnAsync(ITurn t, CancellationToken c) => throw Invoked();
        public ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken c) => throw Invoked();
        public bool HandlesMetadata(Message m) => m.Kind == ReceiptQuery.Kind;
        public ValueTask MetadataAsync(ITurn t, InstanceStatus s, CancellationToken c) => throw Invoked();
        public void TerminateMetadata(object s) => throw Invoked();
        public bool ProtectsDeletion(object s, long n, ICommitClock c) => throw Invoked();
    }
}
