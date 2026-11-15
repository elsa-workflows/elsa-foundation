using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static class KeyChecks
{
    public static async Task Run(string label, IKernelStore store, IKeyAdmission ingress, Func<long, Task> waitUntil, bool exactClock,
        Func<KeyRequest, Task>? atomicFailure = null, Func<long>? trips = null, Func<KeyRequest, Task>? lockedExpiry = null)
    {
        var start = new StartSpec("probe", "parent");
        var scope = Guid.NewGuid().ToString("N");
        var key = "order-" + scope;
        KeyRequest Create(string operation, long window) => new(key, operation, Kinds.Start, Start: start, KeepForMillis: window);
        var first = Create("first", exactClock ? 100 : 1400);
        var worker = new Worker("key-owner", 0, store, new WorkflowInstance([new Probe()]));
        await new DurableHost([worker]).ActivateAsync(0);
        await worker.PollAsync();
        async Task Pump()
        {
            await worker.WokenAsync(null);
            for (int i = 0; i < 10; i++) await worker.RunAsync();
        }
        async Task<InstanceRow?> Row(string id) => (await store.LoadAsync([id])).SingleOrDefault();
        async Task<ProbeState> State(string id) => Json.Read<ProbeState>((await Row(id))!.State);
        async Task Give(string id, string kind)
        {
            Check(await store.AddAsync(new(scope + "-" + kind, new(id), kind)), "failed to enqueue " + kind);
            await Pump();
        }
        async Task<KeyDecision> Accept(KeyRequest request) => await ingress.AcceptAsync(request);
        async Task<KeyDecision> Measured(string stage, KeyRequest request)
        {
            var before = trips?.Invoke();
            var answer = await Accept(request);
            if (before.HasValue) Console.WriteLine($"{label} {stage}: wire trips={trips!() - before}; decision={answer.Outcome}.");
            return answer;
        }
        if (atomicFailure is not null) await atomicFailure(Create("rollback", 1000) with { Key = key + "-fail" });
        var receipt = await Measured("first binding and enqueue", first); // discard its first transport answer and retry before processing the queue
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Accept(first))));
        Check(receipt.Outcome == "accepted" && concurrent.All(r => r == receipt), "unknown answer / parallel replay changed receipt");
        Check((await Measured("changed-digest replay", first with { Payload = "different" })).Outcome == "conflict", "changed request digest did not conflict");
        foreach (var kind in new[] { Kinds.Job, Kinds.JobFinished, Kinds.Meet, Kinds.Publish, Kinds.Event, WorkflowKinds.ChildFinished })
        {
            var op = "invalid-" + kind;
            var invalid = new KeyRequest(key, op, kind);
            foreach (var payload in new string?[] { null, Json.Write(new { Model = "model", Key = "key", Arguments = "{}" }) })
            {
                try { await Accept(invalid with { Payload = payload }); throw new InvalidOperationException("key admission accepted reserved kind " + kind); }
                catch (ArgumentException) { }
            }
            Check(!(await store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100)).Messages.Any(m => m.Message.Id == invalid.WireId),
                "refusal left a poisoned durable message");
            Check((await Accept(invalid with { Kind = "signal", Payload = Json.Write("valid") })).Outcome == "accepted",
                "refusal saved a receipt for an invalid operation");
        }
        var old = receipt.RunId!;
        var cut = receipt.KeepUntil!.Value;
        await Pump();
        var firstChild = (await State(old)).Child!;
        await Give(old, "finish");
        var oldMessage = new KeyRequest(key, "old-command", "signal", Json.Write("old"));
        var delayed = await Measured("resolve and enqueue", oldMessage);
        Check(delayed.RunId == old, "acceptance failed to pin old command");
        // Acceptance wrote a durable message. Give the current owner the Delete locally without polling that message.
        Check(worker.Post(new(scope + "-delete", new(old), Kinds.Delete)), "owner refused local delete");
        await worker.LoadAsync();
        worker.RunTurn(old);
        await worker.FlushAsync();
        Check((await store.LoadAsync([old])).Single().Retired, "physical run was not retired");
        async Task<bool> StillQueuedAtOldId() => (await store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100)).Messages
            .Any(m => m.Message.Id == oldMessage.WireId && m.Message.Target.Id == old);
        Check(await StillQueuedAtOldId(), "retirement consumed the pinned old command");
        var next = Create("next", 20);
        if (exactClock) await waitUntil(cut - 1);
        if (exactClock) Check((await Accept(next)).Outcome == "in-use", "retired key admitted too early");
        Check((await Accept(new(key, "before-rebind", "signal"))).Outcome == "missing", "retired run accepted a new key command");
        await waitUntil(cut);
        var others = Enumerable.Range(0, 8).Select(i => next with { Operation = "race-" + i }).ToArray();
        var winners = await Task.WhenAll(others.Select(r => Task.Run(() => Accept(r))));
        Check(winners.Count(x => x.Outcome == "accepted") == 1 && winners.Count(x => x.Outcome == "in-use") == 7, "racing starts shared the key");
        var fresh = winners.Single(r => r.Outcome == "accepted");
        Check(fresh.RunId != old && (await Measured("receipt replay", first)) == receipt && (await Accept(oldMessage)) == delayed, "replay retargeted or recreated old run");
        Check((await Accept(new(key, "current", "signal", Json.Write("current")))).RunId == fresh.RunId, "new command resolved incorrectly");
        Check(await StillQueuedAtOldId(), "key reuse changed the queued physical target");
        await worker.DisposeAsync();
        worker = new Worker("new-key-owner", 0, store, new WorkflowInstance([new Probe()]));
        await new DurableHost([worker]).ActivateAsync(0);
        await Pump();
        var secondState = await State(fresh.RunId!);
        Check(secondState.Signal == "current" && secondState.Child != firstChild && secondState.Child is not null && (await Row(secondState.Child))?.Parent == fresh.RunId,
            "new command or child's physical identity aliased original run");
        Check((await Row(old)) is { Retired: true } &&
            !(await store.PollAsync(0,long.MaxValue,TimeSpan.Zero,100)).Messages.Any(m => m.Message.Id == oldMessage.WireId) &&
            (await State(fresh.RunId!)).Signal == "current", "delayed accepted command reached the replacement or failed to drain");
        await waitUntil(fresh.KeepUntil!.Value);
        Check((await Accept(Create("too-many", 50))).Outcome == "in-use", "existing run was displaced after its window");
        Check((await Row(old)) is { Retired: true }, "old physical run revived");
        var direct = await store.PollAsync(0, long.MaxValue, TimeSpan.FromDays(1), 100);
        Check(!direct.Messages.Any(m => m.Message.Target.Id == old || m.Message.Target.Id == fresh.RunId), "duplicate admitted messages remain");
        // Measure a separate, genuinely fresh generation without concurrent handshakes on the measured connection.
        var secondKey = key + "-cost";
        var sample = await Accept(Create("sample", 1) with { Key = secondKey });
        await Pump();
        Check(await store.AddAsync(new(scope + "-sample-finish", new(sample.RunId!), "finish")), "sample finish");
        await Pump();
        Check(await store.AddAsync(new(scope + "-sample-delete", new(sample.RunId!), Kinds.Delete)), "sample delete");
        await Pump();
        await waitUntil(sample.KeepUntil!.Value);
        var sample2 = Create("sample2", 1) with { Key = secondKey };
        Check((await Measured("expired retired binding and new generation", sample2)).Outcome == "accepted", "sample rebind");

        // No sweep. Distinct racing operations can acquire only one fresh run at or after the window boundary.
        if (lockedExpiry is not null)
        {
            var held = Create("lock-old", 1) with { Key = key + "-lock" };
            var lockOld = await Accept(held);
            await Pump();
            await store.AddAsync(new(scope + "-lock-finish", new(lockOld.RunId!), "finish")); await Pump();
            await store.AddAsync(new(scope + "-lock-delete", new(lockOld.RunId!), Kinds.Delete)); await Pump();
            await lockedExpiry(held with { Operation = "lock-new" });
        }
        await worker.DisposeAsync();
        Console.WriteLine($"{label}: concurrent replays and claims, key-window expiry without sweep, delayed pinned command, fresh children, takeover and live-run refusal passed.");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
