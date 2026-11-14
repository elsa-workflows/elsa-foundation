using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    // All inherited rows are marked by a real previous owner, then observed by another host.
    private sealed class Takeover
    {
        public long Clock = 100;
        public readonly InstanceRef Actor = InstanceRef.In(0, "takeover-cut");
        public readonly InMemoryStore Store;
        public readonly DelayedCommit Wire;
        public readonly List<string[]> Batches = [];
        public readonly List<string[]> Trials = [];

        public Takeover()
        {
            Store = new(1, () => Clock);
            Wire = new(Store);
            Store.Instances[Actor.Id] = new(Actor.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
        }

        public async Task Add(string kind, long? due = null, string? id = null)
            => await Store.AddAsync(new(id ?? kind, Actor, kind, Due: due));

        public async Task<Worker> Activate(string name, int horizon, int group = 1)
        {
            var owner = new Worker(name, 0, Wire, new WorkflowInstance([new TrialCut()]), new()
            {
                Monotonic = () => Clock, Horizon = TimeSpan.FromMilliseconds(horizon), TrialGroup = group,
                OnTurn = (_, messages, _) => Batches.Add(messages.Select(m => m.Id).ToArray())
            });
            await new DurableHost([owner]).ActivateAsync(0);
            await owner.PollAsync();
            await owner.LoadAsync();
            return owner;
        }

        public async Task MarkPrevious(int horizon, params string[] marked)
        {
            await using var previous = await Activate("previous", horizon);
            await previous.FlushAsync();
            foreach (var id in marked)
                Need(Store.Messages[id].Seen == previous.Epoch && Store.Messages[id].Trial == 0, "previous owner did not mark " + id);
        }

        public void RecordTrial(Worker owner)
        {
            var members = Store.Messages.Values.Where(r => r.Trial == owner.Epoch).Select(r => r.Message.Id).Order().ToArray();
            if (members.Length > 0 && (Trials.Count == 0 || !Trials[^1].SequenceEqual(members))) Trials.Add(members);
            Need(Store.Messages.Values.All(r => r.Attempts == 0), "a graceful handoff charged an attempt");
        }

        public async Task Drain(Worker owner)
        {
            for (var i = 0; i < 24; i++)
            {
                await owner.PollAsync();
                await owner.RunAsync();
                RecordTrial(owner);
            }
        }

        public void Expect(Worker owner, InstanceStatus status, params string[] kinds)
        {
            var row = Store.Instances[Actor.Id];
            Need(row.Status == status && Json.Read<List<string>>(row.State).SequenceEqual(kinds)
                && !owner.Trial.Any() && Store.Messages.Count == 0,
                $"status={row.Status}, state={row.State}, trial=[{string.Join(",", owner.Trial)}], remaining=[{string.Join(",", Store.Messages.Keys)}]");
        }
    }

    private static async Task TakeoverCuts(Func<string, Func<Task>, Task> Check)
    {
        foreach (var extra in new[] { "", Kinds.Resume, Kinds.Suspend, Kinds.Terminate })
        foreach (var wake in new[] { 150, 200 })
            await Check($"two mature controls, extra={extra}, wake={wake}", async () =>
            {
                var f = new Takeover();
                await f.Add("first");
                await f.Add(Kinds.Terminate, 150);
                if (extra != "") await f.Add(extra, 150, "extra");
                await f.Add("earlier", 100);
                await f.MarkPrevious(100, "first", "earlier");
                await f.Add("late");
                await using var owner = await f.Activate("new", 100);
                await owner.FlushAsync();
                f.RecordTrial(owner);
                f.Clock = wake;
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, "first", "earlier", "late");
                Need(f.Trials.Count == 2 && f.Trials[0].SequenceEqual(["first"]) && f.Trials[1].SequenceEqual(["earlier"]),
                    "each suspect needs its own fenced trial");
            });

        foreach (var inheritedTimer in new[] { false, true })
        foreach (var horizon in new[] { 0, 100 })
        foreach (var command in new[] { false, true })
            await Check($"late durable predecessor, inherited={inheritedTimer}, horizon={horizon}, command={command}", async () =>
            {
                var f = new Takeover();
                await f.Add("older", 120);
                await f.Add("selected");
                await f.MarkPrevious(inheritedTimer ? 100 : 0, "selected");
                await using var owner = await f.Activate("new", horizon);
                if (command) await f.Add(Kinds.Terminate, 150);
                await owner.FlushAsync();
                f.RecordTrial(owner);
                Need(owner.Trial.SequenceEqual(["selected"]), "selected did not enter trial");
                f.Clock = 160;
                await f.Drain(owner);
                // When first polled already due, an inherited timer is itself a suspect. Its own trial
                // follows the already fenced member. Otherwise it is an ordinary earlier batch member.
                bool separate = inheritedTimer && horizon == 0;
                f.Expect(owner, command ? InstanceStatus.Cancelled : InstanceStatus.Running,
                    separate ? ["selected", "older"] : ["older", "selected"]);
                Need(f.Trials.Count == (separate ? 2 : 1) && (!separate || f.Trials[1].SequenceEqual(["older"])),
                    "late ordinary input was charged or inherited timer was never tried");
            });

        foreach (var inherited in new[] { false, true })
        foreach (var timerDue in new[] { 140, 165, 190 })
        foreach (var wake in new[] { 150, 200 })
            await Check($"inverted control deadlines, inherited={inherited}, due={timerDue}, wake={wake}", async () =>
            {
                var f = new Takeover();
                await f.Add("selected");
                if (inherited) await f.MarkPrevious(0, "selected");
                await f.Add(Kinds.Resume, 180);
                await f.Add(Kinds.Terminate, 150);
                await f.Add("timer", timerDue);
                await using var owner = await f.Activate("new", 100);
                await owner.FlushAsync();
                f.Clock = wake;
                await f.Drain(owner);
                f.Clock = 200;
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, inherited && timerDue < 150 ? ["selected", "timer"] : ["selected"]);
            });

        foreach (var group in new[] { 1, 4 })
        foreach (var horizon in new[] { 0, 100 })
        foreach (var timerDue in new[] { 103, 105, 110 })
            await Check($"trial group admission, group={group}, horizon={horizon}, timer={timerDue}", async () =>
            {
                var f = new Takeover();
                await f.Add("selected");
                await f.Add("timer", timerDue);
                await f.MarkPrevious(100, "selected", "timer");
                await using var owner = await f.Activate("new", horizon, group);
                owner.Post(new("terminate", f.Actor, Kinds.Terminate, Due: 105));
                f.Clock = 110;
                await owner.PollAsync();
                await owner.FlushAsync();
                var members = owner.Trial.ToArray();
                Need(members.SequenceEqual(["selected"]), "group admitted an input first observed past the control: " + string.Join(",", members));
                await f.Drain(owner);
                // Mixed ingress has first-observation evidence only. A timer prefetched at 100 is ordinary;
                // at equal due its earlier observation wins. A first observation at 110 is already too late.
                f.Expect(owner, InstanceStatus.Cancelled, horizon > 0 && timerDue <= 105 ? ["selected", "timer"] : ["selected"]);
            });

        foreach (var group in new[] { 1, 4 })
            await Check($"mature boundary filters a sequence hole, group={group}", async () =>
            {
                var f = new Takeover();
                await f.Add("selected");
                await f.Add("after", 112);
                await f.Add("before", 103);
                await f.Add(Kinds.Terminate, 105);
                await f.MarkPrevious(100, "selected", "after", "before");
                await using var owner = await f.Activate("new", 0, group);
                f.Clock = 112;
                await owner.PollAsync();
                await owner.FlushAsync();
                f.RecordTrial(owner);
                Need(owner.Trial.SequenceEqual(group == 1 ? ["selected"] : ["selected", "before"]),
                    "pre-cut suspect behind a post-cut suspect was misclassified");
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, "selected", "before");
                Need(f.Batches[0].SequenceEqual(group == 1 ? ["selected"] : ["selected", "before"]),
                    "legitimate group members did not share the first actor batch");
            });

        foreach (var earlyPosted in new[] { false, true })
        foreach (var heldAck in new[] { false, true })
            await Check($"earlier maturity discovered after first boundary, posted={earlyPosted}, held ACK={heldAck}", async () =>
            {
                var f = new Takeover();
                await f.Add("selected");
                await f.MarkPrevious(0, "selected");
                await f.Add(Kinds.Resume, 180);
                await f.Add("between", 165);
                await using var owner = await f.Activate("new", 100);
                await owner.FlushAsync();
                Need(owner.Trial.SequenceEqual(["selected"]), "selection missing");

                async Task EarlyCommand()
                {
                    if (earlyPosted) owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 150));
                    else
                    {
                        await f.Add(Kinds.Terminate, 150);
                        await owner.PollAsync();
                    }
                }

                if (heldAck)
                {
                    f.Clock = 149;
                    f.Wire.DelayNext = true;
                    var flight = owner.RunAsync().AsTask();
                    try
                    {
                        await f.Wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        Need(!flight.IsCompleted && f.Store.Instances[f.Actor.Id].State == "[\"selected\"]", "trial ACK was not held");
                        f.Clock = 200;
                        await EarlyCommand();
                    }
                    finally { f.Wire.Reply.TrySetResult(); }
                    await flight;
                }
                else
                {
                    f.Clock = 200;
                    Need(owner.Ready.Contains(f.Actor.Id), "selected actor not ready at first boundary");
                    await EarlyCommand();
                }
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, "selected");
            });

        foreach (var postedEnd in new[] { false, true })
            await Check($"later deferred control survives first control, posted end={postedEnd}", async () =>
            {
                var f = new Takeover();
                await f.Add("selected");
                await f.MarkPrevious(0, "selected");
                await f.Add(Kinds.Resume, 130);
                await f.Add("after", 175);
                await f.Add("before", 150);
                if (!postedEnd) await f.Add(Kinds.Terminate, 160);
                await using var owner = await f.Activate("new", 100);
                if (postedEnd) owner.Post(new("end", f.Actor, Kinds.Terminate, Due: 160));
                await owner.FlushAsync();
                f.Clock = 200;
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, "selected", "before");
            });
    }
}
