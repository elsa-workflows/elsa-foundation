namespace Kernel.Native.Run;

internal static class HttpOverlapChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable, Modes.Ephemeral })
        foreach (var giveUp in new[] { false, true })
        {
            long now = 0;
            var network = new Network();
            var registry = new WorkflowRegistry()
                .Add(Wf.Workflow("fast", Wf.Sequence("root", Wf.HttpEndpoint("in", "fast"), Wf.Respond("out", "fast"))))
                .Add(Wf.Workflow("slow", Wf.Sequence("root", Wf.HttpEndpoint("in", "slow"), Wf.Delay("wait", 1000), Wf.Respond("out", "slow"))));
            var owner = new Owner("one", 0, new InMemoryStore(1, () => now), registry, network, monotonic: () => now);
            await using var worker = owner.Worker;
            await owner.StartAsync();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = owner.Http.SendAsync(new("POST", "slow"), worker, mode, async () => { await gate.Task; await owner.PumpAsync(poll: true); });
            var waiting = owner.Requests.All.Single();
            // A successful fast call and one whose pump gives up both leave the slow caller alone.
            var fast = await owner.Http.SendAsync(new("POST", "fast"), worker, mode,
                giveUp ? () => Task.CompletedTask : () => owner.PumpAsync(until: () => network.Answers == 1));
            var independent = now < waiting.Deadline && owner.Requests.IsOpen(waiting);
            await owner.PumpAsync();
            now = 1000; gate.SetResult();
            var answer = await slow;
            check(independent && fast.StatusCode == (giveUp ? 504 : 200) && answer.StatusCode == 200 &&
                answer.Body?.GetValue<string>() == "slow" && network.Answers == (giveUp ? 1 : 2) && owner.Requests.All.Count == 0,
                $"{mode}: one caller finishing its pump preserves the other's live deadline, gave up={giveUp}");
        }
        const int workers = 4, deliveries = 256;
        var routes = new Network(); var target = routes.Node("receiver"); var link = routes.From("sender");
        var calls = Enumerable.Range(0, deliveries).Select(_ => target.Open("same", 1000)).ToArray();
        using var barrier = new Barrier(workers); var accepted = 0;
        routes.OnAnswer = _ => Interlocked.Increment(ref accepted);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
        {
            for (var i = worker; i < calls.Length; i += workers)
            {
                barrier.SignalAndWait();
                link.Send("receiver", new Reply(calls[i].Registration, "issued", Request.Response, i));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        check(accepted == deliveries && routes.Sent == deliveries && routes.Answers == deliveries && routes.Hops == deliveries &&
            target.All.Count == 0 && calls.Select((call, i) => call.Answer is int actual && actual == i).All(applied => applied),
            "concurrent network replies count every send, answer and hop exactly once");
    }
}
