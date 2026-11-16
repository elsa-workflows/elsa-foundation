using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Bpmn.Model.State;

namespace Kernel.Bpmn.Run;

internal static class RaceFaultChecks
{
    private sealed class FaultGate
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _release = new();
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource Started(string name) => _started.GetOrAdd(name, _ => New());
        private TaskCompletionSource Release(string name) => _release.GetOrAdd(name, _ => New());
        public Task WaitStarted(string name) => Started(name).Task;
        public void Open(string name) => Release(name).TrySetResult();
        public async ValueTask Gate(IJob job, CancellationToken cancel)
        {
            var name = Json.Read<JobArgs>(job.Arguments!).Service;
            if (name is not ("boom" or "note")) return;
            using var registration = cancel.Register(() => { if (name == "note") Cancelled.TrySetResult(); });
            Started(name).TrySetResult();
            await Release(name).Task;
        }
    }

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var qualified in new[] { false, true })
        foreach (var messageWins in new[] { true, false })
        {
            long now = 1000;
            var store = new InMemoryStore(1, () => now);
            var owner = new Owner("race", 0, store, BpmnCases.Registry(1000), () => now);
            await owner.StartAsync();
            var id = InstanceRef.In(0, $"race-{qualified}-{messageWins}");
            var spec = BpmnModel.Spec("b-race", new JsonObject { ["order"] = "order" }) with
                { Creation = qualified ? new(1, 0, "race") : null };
            owner.Worker.Post(new("start", id, Kinds.Start, Start: spec), fresh: true);
            await owner.PumpAsync(() => store.Instances.TryGetValue(id.Id, out var row) &&
                Json.Read<BpmnState>(row.State).Live.Count == 2 && store.Subscriptions.Count == 1);
            var waiting = store.Instances[id.Id];
            var state = Json.Read<BpmnState>(waiting.State);
            var timer = state.Live.Single(x => x.Value.Kind == WorkKinds.Timer);
            var ticket = store.Messages[InboxKey.Issued(timer.Value.Key)].Message;
            var due = ticket.Due!.Value;
            check(ticket.Target == id && ticket.Kind == BpmnKinds.Timer && ticket.Read<TimerDue>().Handle == timer.Key && due == now + 1000 &&
                store.Subscriptions.Single().Key == BpmnModel.MessageKey("paid", "order"), $"race g{(qualified ? 1 : 0)} has real subscription and future issued timer");
            now = due - 1;
            await owner.Worker.PollAsync(); await owner.PumpAsync(() => true);
            check(store.Instances[id.Id] == waiting, "race stays unchanged before deadline");
            if (messageWins) await Events.PublishAsync(store, new(BpmnModel.MessageKey("paid", "order"), "paid", "{}", due + 10000));
            else now = due;
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted, poll: true);
            var finished = store.Instances[id.Id];
            state = Json.Read<BpmnState>(finished.State);
            check(finished.Status == InstanceStatus.Finished && state.Variables["size"]?.GetValue<string>() == (messageWins ? "big" : "small") &&
                state.Live.Count == 0 && state.Tokens.Tokens.All(t => t.Status == BpmnTokenStatus.Consumed && t.StoppedHandle is null) &&
                store.Subscriptions.Count == 0 && !store.Messages.Values.Any(m => !m.Done && m.Message.Target == id),
                $"race {(messageWins ? "message" : "timer")} wins and clears all work");
            var journal = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).ToArray();
            await Events.PublishAsync(store, new(BpmnModel.MessageKey("paid", "order"), "late", "{}", due + 10000));
            now = due + 1;
            await owner.Worker.PollAsync(); await owner.PumpAsync(() => true);
            check(store.Instances[id.Id] == finished && journal.SequenceEqual(store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position)), "late event and recalled deadline cannot reopen race");
            await owner.Worker.DisposeAsync();
        }

        foreach (var qualified in new[] { false, true })
        {
            long now = 1000;
            var gate = new FaultGate();
            var results = 0;
            var store = new InMemoryStore(1, () => now);
            var owner = new Owner("fault", 0, store, BpmnCases.Registry(1000, name => { if (name == "note") Interlocked.Increment(ref results); }), () => now, new BpmnOptions { Gate = gate.Gate });
            await owner.StartAsync();
            var id = InstanceRef.In(0, $"fault-{qualified}");
            var spec = BpmnModel.Spec("b-fault") with { Creation = qualified ? new(1, 0, "fault") : null };
            owner.Worker.Post(new("start", id, Kinds.Start, Start: spec), fresh: true);
            await owner.PumpAsync(() => gate.WaitStarted("boom").IsCompleted && gate.WaitStarted("note").IsCompleted);
            var state = owner.State(id.Id)!;
            var note = state.Live.Single(x => x.Value.Element == "note").Key;
            var noteDriver = owner.Worker.JobCompletion(id.Id, note, qualified ? 1 : 0) ?? throw new Exception("note driver missing");
            check(!noteDriver.IsCompleted, $"fault g{(qualified ? 1 : 0)} has both actual jobs running");
            gate.Open("boom");
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Faulted && gate.Cancelled.Task.IsCompleted);
            var faulted = store.Instances[id.Id];
            var journal = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).ToArray();
            check(journal.SelectMany(j => Json.Read<List<string>>(j.Entries)).Contains("fault bpmn.work.faulted") && Json.Read<BpmnState>(faulted.State).Live.Count == 0 && gate.Cancelled.Task.IsCompleted && !noteDriver.IsCompleted, "unhandled boom faults and cancels the still-running sibling");
            gate.Open("note");
            await noteDriver.WaitAsync(TimeSpan.FromSeconds(10));
            await owner.PumpAsync(() => true);
            state = Json.Read<BpmnState>(store.Instances[id.Id].State);
            check(results == 1 && state.Variables["notes"]?.GetValue<int>() == 0 && store.Instances[id.Id] == faulted &&
                journal.SequenceEqual(store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position)), "cancelled body actually returns but cannot merge or change faulted row and journal");
            await owner.Worker.DisposeAsync();
        }
    }
}
