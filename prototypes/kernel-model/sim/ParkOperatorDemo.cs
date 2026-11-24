using System.Text.Json;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkOperatorDemo
{
    internal static async Task<int> RunAsync()
    {
        long now = 1_000;
        var store = new InMemoryStore(1, () => now);
        var actor = new ApprovalActor();
        var worker = new Worker("operator-demo", 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { MaxAttempts = 1, MaxBatch = 1, Monotonic = () => now });
        await using var owner = await new DurableHost([worker]).ActivateAsync(0)
            ?? throw new InvalidOperationException("Could not activate the demo partition.");
        var scope = ContractScopes.Bind("approvals");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (requested, kind) => requested.Tenant == scope.Tenant && kind is Contract.Kinds.Recover or Contract.Kinds.Discard);
        Contract.IActorReads reads = new ActorList(store, 1);

        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "approval")), "create", Contract.Kinds.Start);
        Require(started.Outcome == Contract.AdmissionOutcome.Accepted, "Start is accepted through public ingress.");
        var reference = started.Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await DriveAsync();

        await ingress.SendAsync(scope, target, "earlier", "update", options: new(KeepFor: TimeSpan.FromMinutes(1)));
        await DriveAsync();
        Require(store.Messages.Values.Any(row => row.Message.Id == "earlier" && row.Done && row.KeptBy is null),
            "An earlier settled receipt remains stored.");
        await ingress.SendAsync(scope, target, "approval", "approve");
        await DriveAsync();
        await ingress.SendAsync(scope, target, "arrived", "update");
        await DriveAsync();

        var listed = await reads.ListAsync(scope, new(Parked: true));
        Require(listed.Items is [{ Header.Status: Contract.ActorStatus.Faulted, Park: not null, Selection: not null }]
            && listed.Items[0].Header.Ref == reference, "The public parked list identifies the faulted actor and its selection.");
        var parked = listed.Items.Single();
        Console.WriteLine($"Parked at {parked.Park!.At} with fault {parked.Header.Fault}");

        var first = await reads.InboxAsync(scope, reference);
        Require(first.Items is [{ Id: "approval", State: Contract.DeliveryState.Consumed }] && first.Next is not null,
            "The first public inbox page shows the consumed approval.");
        var second = await reads.InboxAsync(scope, reference, first.Next);
        Require(second.Items is [{ Id: "arrived", State: Contract.DeliveryState.Arrived }] && second.Next is null,
            "The next page shows the arrival during Park and excludes settled earlier deliveries.");
        var foreign = ContractScopes.Bind("other-tenant");
        Require((await reads.InboxAsync(foreign, reference)).Items.Count == 0 &&
            (await reads.ListAsync(foreign, new(Parked: true))).Items.Count == 0,
            "Park and kept deliveries are scoped to the actor's tenant.");
        Console.WriteLine($"The operator selected {first.Items[0].Id} from the inbox and left {second.Items[0].Id} to resume normally.");

        actor.ServiceReady = true;
        var retry = new Contract.OperatorCommand(parked.Selection!, Contract.RecoverMode.Retry, [first.Items[0].Handle]);
        var admitted = await ingress.SendAsync(scope, target, "retry-approval", Contract.Kinds.Recover, Json.Write(retry));
        Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, "Public ingress admits the retry command.");
        var recovering = await ReadOpenResolutionAsync();
        var keptDuringRecovery = await reads.InboxAsync(scope, reference);
        Require(recovering is { Header.Status: Contract.ActorStatus.Running, Park.OpenResolution: "recovering" } &&
            keptDuringRecovery.Items is [{ State: Contract.DeliveryState.Consumed }],
            $"The open recovery shows its remaining kept deliveries ({recovering?.Header.Status}, {recovering?.Park?.OpenResolution}, " +
            $"{string.Join(", ", keptDuringRecovery.Items.Select(item => $"{item.Kind}/{item.State}"))}).");
        await DriveAsync();

        var recovered = await reads.GetAsync(scope, reference);
        Require(recovered is { Header.Status: Contract.ActorStatus.Running, Park: null } &&
            (await reads.ListAsync(scope, new(Parked: true))).Items.Count == 0 &&
            (await reads.InboxAsync(scope, reference)).Items.Count == 0,
            "The Park and its kept inbox have cleared.");
        var journal = new List<string>();
        Contract.PageToken? bookmark = null;
        for (var i = 0; i < 32; i++)
        {
            var page = await reads.TailAsync(scope, reference, bookmark);
            if (page.Items.Count == 0) break;
            journal.AddRange(page.Items.Select(item => item.Entry));
            bookmark = page.Next ?? throw new InvalidOperationException("The journal page did not carry a bookmark.");
        }
        Require(journal.Count(entry => entry == JsonSerializer.Serialize("applied approval")) == 1 &&
            journal.Count(entry => entry == JsonSerializer.Serialize("applied arrived")) == 1 &&
            journal.Count(entry => entry == JsonSerializer.Serialize("applied earlier")) == 1,
            "The journal records the selected retry and the arrival once each.");
        Console.WriteLine("The actor is Running. Its journal confirms each delivery was applied once.");

        actor.ServiceReady = false;
        await ingress.SendAsync(scope, target, "new-approval", "approve");
        await DriveAsync();
        var parkedAgain = await reads.GetAsync(scope, reference);
        Require(parkedAgain is { Header.Status: Contract.ActorStatus.Faulted, Park: not null, Selection: not null },
            "The actor parks again when its service is unavailable.");
        var discard = await ingress.SendAsync(scope, target, "discard-approval", Contract.Kinds.Discard,
            Json.Write(new Contract.OperatorCommand(parkedAgain!.Selection!)));
        Require(discard.Outcome == Contract.AdmissionOutcome.Accepted, "Public ingress admits the discard command.");
        var discarding = await ReadOpenResolutionAsync();
        Require(discarding is { Header.Status: Contract.ActorStatus.Faulted, Park.OpenResolution: "discarding" } &&
            (await reads.InboxAsync(scope, reference)).Items is [{ Id: "new-approval", State: Contract.DeliveryState.Consumed }],
            "An open discard is visible with its remaining kept delivery.");
        await DriveAsync();
        Require((await reads.GetAsync(scope, reference)) is { Header.Status: Contract.ActorStatus.Faulted, Park: null } &&
            (await reads.InboxAsync(scope, reference)).Items.Count == 0,
            "Discard clears the retained work and leaves the actor faulted.");
        return 0;

        async Task<Contract.ActorView?> ReadOpenResolutionAsync()
        {
            Contract.ActorView? view = null;
            for (var pass = 0; pass < 8; pass++)
            {
                await PassAsync();
                view = await reads.GetAsync(scope, reference);
                if (view?.Park?.OpenResolution is not null) break;
            }
            return view;
        }

        async Task DriveAsync()
        {
            for (var pass = 0; pass < 16; pass++)
                await PassAsync();
        }

        async Task PassAsync()
        {
            now += 11;
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private static void Require(bool condition, string expectation)
    {
        if (!condition) throw new InvalidOperationException(expectation);
        Console.WriteLine($"ok {expectation}");
    }

    private sealed class ApprovalActor : Contract.IActorType
    {
        public string Type => "operator-approval";
        public bool ServiceReady { get; set; }
        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
            {
                if (input.Kind == Contract.Kinds.Start) continue;
                if (input.Kind == "approve" && !ServiceReady) throw new InvalidOperationException("Approval service unavailable");
                turn.Record("applied " + input.Id);
            }
            return ValueTask.CompletedTask;
        }
    }
}
