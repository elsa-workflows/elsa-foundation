namespace Kernel.Native.Run;

/// <summary>Park's committed lifecycle, exercised through actual workers and the storage contract.</summary>
public static class ParkChecks
{
    private sealed class ProbeState
    {
        public int Value { get; set; } = 7;
    }

    private sealed class Probe : Kernel.Sim.Model<ProbeState>
    {
        public override string Key => "park-probe";
        protected override void Turn(ITurn turn, ProbeState state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start)
                {
                    turn.Subscribe("approval", "waiting");
                    turn.Record("checkpoint");
                }
                else if (message.Kind == "explode") throw new InvalidOperationException("probe failed");
                else state.Value++;
            }
        }
    }

    private sealed class World
    {
        public long Time = 100;
        public readonly InMemoryStore Store;
        public readonly Worker First;
        public World()
        {
            Store = new InMemoryStore(1, () => Time);
            First = Owner("first");
        }
        public Worker Owner(string name) => new(name, 0, Store, new WorkflowInstance([new Probe()]),
            new WorkerOptions { Monotonic = () => Time, MaxAttempts = 1 });
        public async Task Arm(History history = History.Full)
        {
            await new DurableHost([First]).ActivateAsync(0);
            await First.PollAsync();
            First.Post(new Message("start", InstanceRef.In(0, "probe"), Kinds.Start,
                Start: new StartSpec("park-probe", "waiting", Parent: "0/parent", History: history)), fresh: true);
            await Pump(First, () => Store.Subscriptions.Count == 1);
        }
        public async Task Send(Worker worker, string kind)
        {
            await Store.AddAsync(new Message(kind, InstanceRef.In(0, "probe"), kind));
            await worker.WokenAsync(null);
        }
    }

    private static async Task Pump(Worker worker, Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++) await worker.RunAsync();
        if (!done()) throw new InvalidOperationException("park witness did not reach its condition");
    }

    private static void Require(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
    }

    private static async Task CheckPark(World world, Worker owner, string before, string delivery = "explode")
    {
        var oldJournal = world.Store.Journal.Values.Where(j => j.Instance == "0/probe").ToArray();
        var position = world.Store.Partitions[0].Position;
        await Pump(owner, () => world.Store.Instances["0/probe"].Status == InstanceStatus.Faulted);
        var row = world.Store.Instances["0/probe"];
        Require(row.State == before && row.Fault is not null, "Park must retain the committed checkpoint and fault");
        Require(world.Store.Subscriptions.All(s => s.Instance != row.Id), "Park must remove every active subscription");
        Require(!world.Store.Messages.ContainsKey(delivery), "the failing delivery must be consumed with the fault");
        Require(world.Store.Messages.Values.Any(m => m.Message.Target.Id == "0/parent" && m.Message.Kind == WorkflowKinds.ChildFinished &&
            m.Message.Read<ChildFinished>() is { Child: "0/probe", Status: InstanceStatus.Faulted }), "parent must receive the faulted-child notice");
        Require(oldJournal.All(j => world.Store.Journal.Values.Contains(j)), "Park must retain existing history");
        var added = world.Store.Journal.Values.Where(j => j.Instance == row.Id && !oldJournal.Contains(j)).ToArray();
        Require(row.History switch
        {
            History.Full => added.Length == 1 && Json.Read<string[]>(added[0].Entries).SequenceEqual(["parked: " + row.Fault]),
            History.State => added.Length == 1 && Json.Read<string[]>(added[0].Entries).Length == 0,
            History.Minimal => added.Length == 0 && world.Store.Partitions[0].Position == position,
            _ => false,
        }, "Park must honor the selected history level at its commit");
        await Events.PublishAsync(world.Store, new EventRow("approval", "after-park", "true", world.Time + 60_000));
        Require(!world.Store.Messages.Values.Any(m => m.Message.Kind == Kinds.Event), "later publishes must not route to the parked instance");
        await world.Store.AddAsync(new Message("already-routed", new InstanceRef(row.Id), Kinds.Event, "true"));
        await owner.WokenAsync(null);
        for (var i = 0; i < 5; i++) await owner.RunAsync();
        Require(world.Store.Instances[row.Id] == row, "an already-routed event must not execute or alter the faulted checkpoint");
        Require(!world.Store.Messages.ContainsKey("already-routed"), "an already-routed event must actually be consumed");
    }

    public static async Task<int> RunAsync()
    {
        var failures = 0;
        async Task Case(string label, Func<Task> run)
        {
            try { await run(); Console.WriteLine("pass " + label); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + label + ": " + error.Message); }
        }
        foreach (var history in new[] { History.Full, History.State, History.Minimal })
            await Case("throwing turn, " + history, async () =>
            {
                var world = new World();
                await world.Arm(history);
                var before = world.Store.Instances["0/probe"].State;
                await world.Send(world.First, "explode");
                await CheckPark(world, world.First, before);
            });
        await Case("definite storage refusal", async () =>
        {
            var world = new World();
            await world.Arm();
            var before = world.Store.Instances["0/probe"].State;
            world.Store.Refuses = row => row.Status == InstanceStatus.Running && Json.Read<ProbeState>(row.State).Value == 8;
            await world.Send(world.First, "advance");
            await CheckPark(world, world.First, before, "advance");
        });
        await Case("poison threshold after a lost trial owner", async () =>
        {
            var world = new World();
            await world.Arm();
            var before = world.Store.Instances["0/probe"].State;
            await world.Send(world.First, "advance");
            await world.First.PollAsync();
            await world.First.LoadAsync();
            await world.First.FlushAsync(); // mark the delivery without running it
            world.Time += 11_000;
            var second = world.Owner("second");
            await new DurableHost([second]).ActivateAsync(0);
            await second.PollAsync();
            await second.LoadAsync();
            await second.FlushAsync(); // inherited delivery is put on trial, without running it
            Require(world.Store.Messages["advance"].Trial == second.Epoch, "the witness must actually lose an owner with work on trial");
            world.Time += 11_000;
            var third = world.Owner("third");
            await new DurableHost([third]).ActivateAsync(0);
            Require(world.Store.Messages["advance"].Attempts == 1, "the lost trial must count one attempt");
            await third.WokenAsync(null);
            await CheckPark(world, third, before, "advance");
        });
        await Case("suspension preserves the wait", async () =>
        {
            var world = new World();
            await world.Arm();
            await world.Send(world.First, Kinds.Suspend);
            await Pump(world.First, () => world.Store.Instances["0/probe"].Suspended);
            Require(world.Store.Subscriptions.Count == 1, "suspension must preserve subscriptions");
            Require(world.Store.Instances["0/probe"].Status == InstanceStatus.Running, "suspension is not a fault");
        });
        Console.WriteLine($"park: {6 - failures} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
