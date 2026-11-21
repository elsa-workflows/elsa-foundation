using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class JobResultSizeChecks
{
    private const int PayloadLimit = 16 * 1024 * 1024 - 2 * 24 * 1024;
    private const string Key = "job";
    private const string Oversized = "result too large";

    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        var overhead = Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(Key, Json.Write(""), null)));
        await Run("exact", new string('x', PayloadLimit - overhead), false);
        await Run("one byte over", new string('x', PayloadLimit - overhead + 1), true);
        var escapedOverhead = Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(Key, Json.Write("&"), null)));
        await Run("escaped one byte over", new string('x', PayloadLimit - escapedOverhead + 1) + "&", true);

        async Task Run(string shape, string value, bool tooLarge)
        {
            var encoded = Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(Key, Json.Write(value), null)));
            check(encoded == PayloadLimit + (tooLarge ? 1 : 0), $"{shape}: fixture meets the stored payload boundary");
            await Schedule(value, false, tooLarge, flaws, (ok, name) => check(ok, $"{shape}, deliverable: {name}"));
            await Schedule(value, true, tooLarge, flaws, (ok, name) => check(ok, $"{shape}, suspended: {name}"));
        }
    }

    private static async Task Schedule(string value, bool suspended, bool tooLarge, Flaw flaws, Action<bool, string> check)
    {
        const long now = 1_701_200_000_000;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe(value);
        var requests = new Requests("job-result-size");
        var worker = new Worker("job-result-size", 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => 0, Flaws = flaws });
        var scope = new Contract.BoundScope("sizes");
        Contract.IIngress edge = new InMemoryIngress(store, requests, "job-result-size", (selected, _) => selected == scope);
        var started = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "running", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump();
        var row = store.Instances.Values.Single();
        var ticket = InboxKey.Ticket(row.Id, Key, row.Creation);
        if (suspended)
        {
            var suspend = await edge.SendAsync(scope, target, "suspend", Contract.Kinds.Suspend);
            check(suspend.Outcome == Contract.AdmissionOutcome.Accepted, "the operator suspend is admitted");
            await Pump();
        }
        var finished = worker.JobCompletion(row.Id, Key, row.Creation)!;
        actor.Release.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(5));
        await Pump();
        check(!store.Messages.ContainsKey(ticket), "the completion consumes the original ticket");
        if (suspended)
        {
            var result = store.Messages.GetValueOrDefault(InboxKey.JobResult(row.Id, Key, row.Creation));
            check(result is { Done: false, Message.FoundationJobResult: true } &&
                Encoding.UTF8.GetByteCount(result.Message.Payload!) <= PayloadLimit && actor.Completions == 0,
                "a bounded authenticated result is stored without entering the suspended actor");
            var resume = await edge.SendAsync(scope, target, "resume", Contract.Kinds.Resume);
            check(resume.Outcome == Contract.AdmissionOutcome.Accepted, "the operator resume is admitted");
            await Pump();
        }
        check(actor.Entries == 1 && actor.Completions == 1 && actor.Observed == (tooLarge ? Oversized : "original") &&
            store.Instances.Values.Single().Jobs is null && !store.HasFoundationJobResult(row.Id, row.Creation, Key),
            "one physical attempt settles as one bounded completion and clears the registry");

        async Task Pump()
        {
            for (var step = 0; step < 8; step++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
            }
        }
    }

    private sealed class Probe(string value) : Contract.IActorType
    {
        public string Type => "job-result-size";
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entries { get; private set; }
        public int Completions { get; private set; }
        public string? Observed { get; private set; }

        public bool Withholds(Contract.ActorHeader header) => header.TypeData == "suspended";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.StartJob(Key, timeout: TimeSpan.FromSeconds(1));
                        break;
                    case Contract.Kinds.Suspend or Contract.Kinds.Resume:
                        turn.SetHeader(message.Kind == Contract.Kinds.Suspend ? "suspended" : "running", turn.Header.Labels);
                        break;
                    case Contract.Kinds.JobFinished:
                        var completion = Json.Read<Contract.JobFinished>(message.Payload!);
                        Completions++;
                        Observed = completion.Error ?? (Json.Read<string>(completion.Result!) == value ? "original" : "wrong result");
                        break;
                }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Entries++;
            await Release.Task.WaitAsync(cancellationToken);
            return value;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
