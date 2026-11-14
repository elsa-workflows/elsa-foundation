using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private static async Task UnknownJobCuts(Func<string, Func<Task>, Task> check)
    {
        foreach (var applied in new[] { false, true })
        foreach (var post in new[] { false, true })
        foreach (var terminalTimer in new[] { false, true })
        foreach (var fresh in new[] { false, true })
            await check($"uncertain recovery checkpoint: applied={applied}, post={post}, terminal timer={terminalTimer}, fresh={fresh}", async () =>
            {
                var model = new Spawner(finish: false, wait: true, finishOnTimer: terminalTimer);
                var f = new JobCut(model);
                await f.Add("spawn");
                var ticket = Worker.TicketId(f.Actor.Id, "job");
                await using (var issuer = await f.Activate("issuer"))
                {
                    await issuer.FlushAsync();
                    await issuer.RunAsync();
                }
                await using var lost = await f.Activate("lost", attempts: 1, lease: 100);
                await lost.FlushAsync();
                f.ExpectTrial(lost, ticket);
                await f.Add("later", 220);
                if (!post) await f.Add(Kinds.Terminate, 215);

                f.Clock = 210;
                var faults = new CutFaultStore(f.Wire);
                Worker Replacement() => new("uncertain", 0, faults, new WorkflowInstance([model]), new()
                {
                    Monotonic = () => f.Clock, MaxAttempts = 1, Horizon = TimeSpan.FromMilliseconds(100)
                });
                await using var owner = Replacement();
                await new DurableHost([owner]).ActivateAsync(0);
                await owner.PollAsync();
                await owner.LoadAsync();
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 215)), "post refused");
                await owner.FlushAsync();
                var epoch = owner.Epoch;
                Need(f.Store.Messages[ticket].Attempts == 1 && f.Store.Messages["later"].Seen == epoch
                    && !owner.Trial.Any(), "expected an exhausted ticket and a marked timer");
                owner.RunTurn(f.Actor.Id);
                Need(owner.Queued == 1, "error was not queued before termination matured");
                faults.Arm(applied ? "unknown-after" : "unknown-before");
                if (applied)
                {
                    f.Wire.DelayNext = true;
                    var flight = owner.FlushAsync();
                    try
                    {
                        await f.Wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Need(!flight.IsCompleted && !f.Store.Messages.ContainsKey(ticket)
                            && Json.Read<List<string>>(f.Store.Instances[f.Actor.Id].State).SequenceEqual(["spawn", Kinds.JobFinished]),
                            "error checkpoint did not land before its answer was lost");
                        f.Clock = 230;
                    }
                    finally { f.Wire.Reply.TrySetResult(); }
                    await flight;
                }
                else
                {
                    await owner.FlushAsync();
                    Need(f.Store.Messages.ContainsKey(ticket), "unapplied checkpoint consumed the ticket");
                    f.Clock = 230;
                }
                Need(!owner.Owns, "unknown outcome did not reset ownership");
                await using var freshOwner = fresh ? Replacement() : null;
                var recovered = freshOwner ?? owner;
                await new DurableHost([recovered]).ActivateAsync(0);
                Need(recovered.Epoch > epoch && f.Store.Messages["later"].Attempts == 0, "same-node retake charged the timer");
                await f.Drain(recovered);
                if (post)
                    f.Expect(recovered, terminalTimer ? InstanceStatus.Finished : InstanceStatus.Running, "spawn", Kinds.JobFinished, "later");
                else
                    f.Expect(recovered, InstanceStatus.Cancelled, applied ? ["spawn", Kinds.JobFinished] : ["spawn"]);
                Need(model.Starts == 2, "exhausted driver restarted");
            });
    }
}
