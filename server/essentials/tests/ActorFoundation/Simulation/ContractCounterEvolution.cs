using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class ContractActorWorkload
{
    private sealed record FirstForward(string Token, string Receiver);

    internal static async Task<int> RunEvolution(int from, int seeds, int partitions, Flaw flaws)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            try
            {
                await RunEvolutionSeed(seed, partitions, flaws);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public Counter evolution seed {seed} failed at {partitions} virtual partitions ({flaws}). {error.Message}");
                return 1;
            }
        }
        Console.WriteLine($"Public Counter evolution passed {seeds} seeds from {from} at {partitions} virtual partitions. Each seed upgraded, rolled back and replayed a first forward.");
        return 0;
    }

    private static async Task RunEvolutionSeed(int seed, int partitions, Flaw flaws)
    {
        var speculative = new List<Contract.ActorRef>();
        var older = new PublicWorkflow([new CounterModel(1)]);
        var newer = new PublicWorkflow([new CounterModel(2, flaws.HasFlag(Flaw.SkipUpgrade), speculative.Add)]);
        await using var driver = new ContractWorkload.InMemoryDriver(partitions, flaws, older);
        var scope = ContractScopes.Bind("evolution");
        var runtime = new PublicRuntime(driver.Ingress, driver.Reads);
        var counter = await Start("evolving-counter");
        var bystander = await Start("spare-counter");
        await driver.Start([(scope, counter), (scope, bystander)]);
        await driver.Pass(12);

        var count = new Random(seed).Next(2, 9);
        var prefix = Enumerable.Range(0, count).Select(value => $"old-{value}").ToArray();
        foreach (var token in prefix) await Add(counter, token);
        await driver.Pass(12);
        Require(await Version(counter) == "1" && (await CounterJournal(counter)).Select(entry => entry.Token).SequenceEqual(prefix),
            "the legacy owner did not write its version-one prefix");

        await driver.TakeOver(newer);
        await Add(counter, "upgrade");
        var landed = (seed & 1) == 0;
        await driver.LoseTurnCommit(landed, stopAfterLoss: true);
        Require(await Version(counter) == (landed ? "2" : "1"),
            "the state version did not follow the same commit outcome as the turn");
        await driver.TakeOver(newer);
        await driver.Pass(12);
        var beforeRollback = await CounterJournal(counter);
        Require(await Version(counter) == "2" && beforeRollback.Select(entry => entry.Token).SequenceEqual(prefix.Append("upgrade")) &&
            beforeRollback.Select(entry => entry.Ordinal).SequenceEqual(Enumerable.Range(1, count + 1)),
            "version two did not inherit the old token count exactly once");

        await driver.TakeOver(older);
        await Add(counter, "held-by-older-owner");
        await driver.Pass(12);
        Require(await Version(counter) == "2" &&
            (await CounterJournal(counter)).SequenceEqual(beforeRollback) &&
            (await driver.Reads.InboxAsync(scope, counter)).Items is [{ Id: "held-by-older-owner", State: Contract.DeliveryState.Pending }],
            "an older owner applied or downgraded the newer checkpoint");
        await driver.TakeOver(newer);
        await driver.Pass(12);
        Require((await CounterJournal(counter)).Select(entry => entry.Ordinal).SequenceEqual(Enumerable.Range(1, count + 2)),
            "the compatible successor failed to apply the withheld input once");

        await ForwardTo("first-forward");
        await driver.LoseTurnCommit(landed: false, stopAfterLoss: true);
        Require(speculative is [var initial] &&
            await driver.Reads.GetAsync(scope, initial) is null && (await driver.Reads.InboxAsync(scope, initial)).Items.Count == 0 &&
            (await Entries<FirstForward>(driver, scope, counter, nameof(FirstForward.Receiver))).Count == 0,
            "an uncommitted start and forward escaped the sender's turn");
        await driver.TakeOver(newer);
        await driver.Pass(12);
        Require(speculative is [var lost, var replayed] && lost != replayed &&
            await driver.Reads.GetAsync(scope, lost) is null &&
            (await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(lost), "stale", "add", Json.Write(new Add("stale"))))
                .Outcome == Contract.AdmissionOutcome.Refused,
            "an aborted creator's reference became routable after replay");

        var first = (await Entries<FirstForward>(driver, scope, counter, nameof(FirstForward.Receiver))).Single();
        var receiver = new Contract.ActorRef(first.Receiver);
        Require(receiver == speculative[1], "the committed journal did not publish the replay's exact reference");
        await driver.StartElsewhere([(scope, receiver)]);
        await driver.Pass(12);
        await ForwardTo("second-forward");
        await driver.LoseTurnCommit(landed: true, stopAfterLoss: true);
        await driver.TakeOver(newer);
        await driver.Pass(12);
        var forwards = await Entries<FirstForward>(driver, scope, counter, nameof(FirstForward.Receiver));
        var receipts = await CounterJournal(receiver);
        Require(forwards.Select(entry => entry.Token).SequenceEqual(new[] { "first-forward", "second-forward" }) &&
            forwards.All(entry => entry.Receiver == receiver.Id) && speculative.Count == 2 &&
            (await driver.Reads.GetAsync(scope, receiver)) is { Header: { Parent: null, Status: Contract.ActorStatus.Running } } &&
            receipts.Select(entry => entry.Token).SequenceEqual(new[] { "first-forward", "second-forward" }) &&
            receipts.All(entry => entry.Sender == counter.Id) &&
            receipts.Select(entry => entry.Ordinal).SequenceEqual(new[] { 1, 2 }),
            "the committed start and forwards did not reach one detached receiver in sender order");

        async Task<Contract.ActorRef> Start(string key)
        {
            var accepted = await runtime.StartAsync(scope, "start", new Models.WorkflowStart("counter", "c"), key);
            Require(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, $"start {key} was refused");
            return accepted.Actor!.Value;
        }

        Task Add(Contract.ActorRef to, string token) => Send(to, token, "add", Json.Write(new Add(token)));
        Task ForwardTo(string token) => Send(counter, token, "start-forward", Json.Write(new Forward("receiver", token)));

        async Task Send(Contract.ActorRef to, string token, string kind, string payload)
        {
            var target = new Contract.Target.Ref(to);
            var first = await driver.Ingress.SendAsync(scope, target, token, kind, payload);
            var retry = await driver.Ingress.SendAsync(scope, target, token, kind, payload);
            Require(first.Outcome == Contract.AdmissionOutcome.Accepted && retry.Outcome == Contract.AdmissionOutcome.Duplicate,
                $"admission did not deduplicate {token}");
        }

        async Task<string?> Version(Contract.ActorRef reference) =>
            (await driver.Reads.GetAsync(scope, reference))?.Header is { } header
                ? System.Text.Json.Nodes.JsonNode.Parse(header.TypeData)?["Version"]?.ToJsonString()
                : null;

        Task<List<CounterEntry>> CounterJournal(Contract.ActorRef reference) =>
            Entries<CounterEntry>(driver, scope, reference, nameof(CounterEntry.Ordinal));
    }
}
