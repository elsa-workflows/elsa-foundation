using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using ContractEffectKey = Kernel.Model.Contracts.EffectKey;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Kernel.Native.Binding;

namespace Kernel.Sim;

internal static class NativeTenantEffectChecks
{
    private const string CallerKey = "invoice-9";
    private const string Operation = "start-invoice";
    private const string Definition = "send-invoice";
    private const string HttpUrl = "https://receiver.example.invalid/invoices";
    private const string MailTo = "invoices@receiver.example.invalid";

    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_702_350_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var reads = new ActorList(store, 32);
        var runtime = OpenIngress("first");
        var blue = new Contract.BoundScope("blue");
        var green = new Contract.BoundScope("green");
        var workflow = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Sequence("root",
            Wf.Delay("wait", Expr("input.delay")),
            Wf.Http("http", HttpUrl, Expr("input.payload")),
            Wf.Email("mail", MailTo, "Invoice", Expr("input.payload")),
            Wf.Set("out", "output", Expr("input.payload"))), ("output", null)));
        var receiver = new FakeReceiver();

        var blueReceipt = await Start(blue, "blue-invoice", 10);
        var greenReceipt = await Start(green, "green-invoice", 25);
        if (blueReceipt is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } a } ||
            greenReceipt is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } b } || a == b)
        {
            check(false, "T5 the colliding public starts must admit two distinct exact actor lives");
            return;
        }
        check(true, "T5 both tenants admitted invoice-9 with the same operation id and distinct exact references");

        var blueAddress = store.FoundationTarget(blue.Tenant, a.Id)!.Id;
        var dropped = false;
        var firstWire = new CountingStore(store, static () => { }, (batch, landed) =>
        {
            if (landed || dropped || receiver.Arrivals.Count != 2 ||
                !batch.Turns.Any(turn => turn.Instance == blueAddress && turn.Row?.Status == InstanceStatus.Finished)) return;
            dropped = true;
            throw new StoreException(Failure.Unknown, "The outbound completion was lost before the store executed it.");
        });
        await using var first = await Own("first", firstWire);
        await Drive(first);
        var waitingA = Checkpoint(blue, a);
        var waitingB = Checkpoint(green, b);
        check(waitingA is { Status: InstanceStatus.Running } && waitingB is { Status: InstanceStatus.Running } &&
            receiver.Arrivals.Count == 0, "both Native actors committed their initial wait before issuing any effects");

        now += 10;
        await Drive(first, () => dropped);
        check(dropped && receiver.Arrivals is [{ Owner: "first", Actor: var firstHttp, Channel: Channel.Http },
            { Owner: "first", Actor: var firstMail, Channel: Channel.Mail }] && firstHttp == a && firstMail == a &&
            Checkpoint(blue, a) == waitingA && Checkpoint(green, b) == waitingB,
            "the first owner sent both blue effects while the store kept both earlier checkpoints");

        var earlierEpoch = store.Partitions[0].Epoch;
        now = store.Nodes["first"].LeaseUntil + 1;
        await using var successor = await Own("successor", store);
        check(store.Partitions[0].Owner == "successor" && store.Partitions[0].Epoch > earlierEpoch,
            "a second worker acquired a newer fence after the first lease expired");
        await Drive(successor);

        var sends = receiver.Arrivals;
        check(sends.Count == 6 && sends.Where(send => send.Owner == "successor" && send.Actor == a).Select(send => send.Channel)
            .SequenceEqual([Channel.Http, Channel.Mail]) &&
            sends.Where(send => send.Owner == "successor" && send.Actor == b).Select(send => send.Channel)
                .SequenceEqual([Channel.Http, Channel.Mail]),
            "the successor reissued both blue effects and issued both green effects");
        check(receiver.Applied.Count == 4 && CheckDelivery(a, "blue-invoice", copies: 2) && CheckDelivery(b, "green-invoice", copies: 1),
            "the fake applied one HTTP request and one email per tenant using distinct Idempotency-Key and Message-ID values");

        var finalA = Checkpoint(blue, a);
        var finalB = Checkpoint(green, b);
        var blueJournal = await reads.TailAsync(blue, a);
        var greenJournal = await reads.TailAsync(green, b);
        check(Completed(finalA, waitingA, "blue-invoice", blueJournal) && Completed(finalB, waitingB, "green-invoice", greenJournal) &&
            await reads.GetAsync(blue, b) is null && await reads.GetAsync(green, a) is null,
            "the successor committed each exact life once with its own Native output and activity history");

        runtime = OpenIngress("successor");
        var blueReplay = await Start(blue, "blue-invoice", 10);
        var greenReplay = await Start(green, "green-invoice", 25);
        var blueConflict = await Start(blue, "replacement", 10);
        var greenConflict = await Start(green, "replacement", 25);
        await Drive(successor);
        check(blueReplay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var replayA } && replayA == a &&
            greenReplay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var replayB } && replayB == b &&
            blueConflict is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Conflict } &&
            greenConflict is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Conflict } &&
            Checkpoint(blue, a) == finalA && Checkpoint(green, b) == finalB && sends.Count == 6 &&
            (await reads.TailAsync(blue, a)).Items.SequenceEqual(blueJournal.Items) &&
            (await reads.TailAsync(green, b)).Items.SequenceEqual(greenJournal.Items),
            "each tenant's exact retry returned its original receipt and changed inputs conflicted without further effects");

        PublicRuntime OpenIngress(string node) => new(new InMemoryIngress(store, new Requests(node), node), reads);
        ValueTask<Contract.Admission> Start(Contract.BoundScope scope, string payload, int delay) =>
            runtime.StartAsync(scope, Operation,
                NativeExecutionModel.Start(Definition, Modes.Balanced, new JsonObject { ["payload"] = payload, ["delay"] = delay }),
                callerKey: CallerKey);
        InstanceRow? Checkpoint(Contract.BoundScope scope, Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row;

        async ValueTask<IScopeActivation> Own(string node, IKernelStore wire)
        {
            var model = new NativeExecutionModel(workflow, outbound: effect => receiver.Receive(node, effect));
            var worker = new Worker(node, 0, wire, new ActorAdapter(store, new PublicWorkflow([model])),
                new WorkerOptions { Monotonic = () => now - start, Lease = TimeSpan.FromMilliseconds(100) });
            return await new DurableHost([worker]).ActivateAsync(0) ?? throw new InvalidOperationException("The fixture could not acquire the partition.");
        }

        bool CheckDelivery(Contract.ActorRef actor, string payload, int copies)
        {
            var httpKey = ContractEffectKey.For(actor.Id, "http", ContractEffectKey.RootPath, 0);
            var mailKey = SendEmail.MessageId(ContractEffectKey.For(actor.Id, "mail", ContractEffectKey.RootPath, 0));
            return sends.Count(send => send is { Channel: Channel.Http, Destination: HttpUrl } &&
                    send.Actor == actor && send.Key == httpKey && send.Payload == payload) == copies &&
                sends.Count(send => send is { Channel: Channel.Mail, Destination: MailTo } &&
                    send.Actor == actor && send.Key == mailKey && send.Payload == payload) == copies &&
                receiver.Applied.GetValueOrDefault((Channel.Http, httpKey)) == payload &&
                receiver.Applied.GetValueOrDefault((Channel.Mail, mailKey)) == payload;
        }
    }

    private static bool Completed(InstanceRow? row, InstanceRow? original, string output, Contract.Page<Contract.JournalEntry> journal)
    {
        if (row is not { Status: InstanceStatus.Finished } || original is null ||
            (row.Id, row.Creation, row.Revision) != (original.Id, original.Creation, original.Revision + 1) ||
            Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() != output) return false;
        var entries = journal.Items.Select(entry => Json.Read<string>(entry.Entry)).ToArray();
        return entries.Count(entry => entry.StartsWith("c:", StringComparison.Ordinal) && entry.EndsWith(":http", StringComparison.Ordinal)) == 1 &&
            entries.Count(entry => entry.StartsWith("c:", StringComparison.Ordinal) && entry.EndsWith(":mail", StringComparison.Ordinal)) == 1 &&
            entries.Count(entry => entry == "end") == 1;
    }

    private static async Task Drive(IScopeActivation owner, Func<bool>? stop = null)
    {
        for (var pass = 0; pass < 20 && stop?.Invoke() != true; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private enum Channel { Http, Mail }

    private sealed record Delivery(string Owner, Contract.ActorRef Actor, Channel Channel, string Key, string Destination, string Payload);

    private sealed class FakeReceiver
    {
        internal List<Delivery> Arrivals { get; } = [];
        internal Dictionary<(Channel, string), string> Applied { get; } = [];

        internal void Receive(string owner, NativeOutbound outgoing)
        {
            var (channel, key, destination, payload) = outgoing.Effect switch
            {
                HttpSendEffect { Request: var request } when request.Method == "POST" =>
                    (Channel.Http, request.Headers[SendHttpRequest.IdempotencyKeyHeader], request.Url, request.Body!.GetValue<string>()),
                EmailSendEffect { Mail: var mail } =>
                    (Channel.Mail, mail.MessageId, mail.To, mail.Body),
                _ => throw new InvalidOperationException("The fake receiver only accepts invoice POSTs and email.")
            };
            Applied.TryAdd((channel, key), payload);
            Arrivals.Add(new(owner, outgoing.Actor, channel, key, destination, payload));
        }
    }
}
