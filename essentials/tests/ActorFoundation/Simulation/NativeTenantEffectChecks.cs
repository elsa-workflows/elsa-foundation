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
        var blue = ContractScopes.Bind("blue");
        var green = ContractScopes.Bind("green");
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
        var bluePrefix = await reads.TailAsync(blue, a);
        var greenPrefix = await reads.TailAsync(green, b);
        if (!Waiting(waitingA, bluePrefix) || !Waiting(waitingB, greenPrefix) ||
            bluePrefix.Next is not { } blueBookmark || greenPrefix.Next is not { } greenBookmark || receiver.Arrivals.Count != 0)
        {
            check(false, "both Native actors must commit their initial wait before effects and expose an exact journal bookmark");
            return;
        }
        check(true, "both Native actors committed their initial wait and exposed an exact journal bookmark before effects");

        now += 10;
        await Drive(first, () => dropped);
        check(dropped && receiver.Arrivals is [{ Owner: "first", Actor: var firstHttp, Channel: Channel.Http },
            { Owner: "first", Actor: var firstMail, Channel: Channel.Mail }] && firstHttp == a && firstMail == a &&
            Checkpoint(blue, a) == waitingA && Checkpoint(green, b) == waitingB &&
            await EmptyTail(blue, a, blueBookmark) && await EmptyTail(green, b, greenBookmark),
            "the first owner sent both blue effects while the store kept both original checkpoints and journal tails");

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
        var blueSuffix = await reads.TailAsync(blue, a, blueBookmark);
        var greenSuffix = await reads.TailAsync(green, b, greenBookmark);
        check(Completed(finalA, waitingA, "blue-invoice") && Completed(finalB, waitingB, "green-invoice") &&
            await reads.GetAsync(blue, b) is null && await reads.GetAsync(green, a) is null,
            "the successor committed each exact life once with its own Native output");
        check(FinalJournal(finalA, blueSuffix) && FinalJournal(finalB, greenSuffix) &&
            await JournalMatches(blue, a, bluePrefix, blueSuffix) && await JournalMatches(green, b, greenPrefix, greenSuffix),
            "both journal suffixes contain ordered HTTP completion, email completion and ending at the final revision");

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
            await JournalMatches(blue, a, bluePrefix, blueSuffix) && await JournalMatches(green, b, greenPrefix, greenSuffix),
            "exact retries returned each original receipt and changed inputs conflicted without new effects or journal entries");

        PublicRuntime OpenIngress(string node) => new(new InMemoryIngress(store, new Requests(node), node), reads);
        ValueTask<Contract.Admission> Start(Contract.BoundScope scope, string payload, int delay) =>
            runtime.StartAsync(scope, Operation,
                NativeExecutionModel.Start(Definition, Modes.Balanced, new JsonObject { ["payload"] = payload, ["delay"] = delay }),
                callerKey: CallerKey);
        InstanceRow? Checkpoint(Contract.BoundScope scope, Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row;
        async ValueTask<bool> EmptyTail(Contract.BoundScope scope, Contract.ActorRef actor, Contract.PageToken? bookmark) =>
            bookmark is { } token && await reads.TailAsync(scope, actor, token) is { Items.Count: 0, Next: null };
        async ValueTask<bool> JournalMatches(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.Page<Contract.JournalEntry> prefix, Contract.Page<Contract.JournalEntry> suffix) =>
            (await reads.TailAsync(scope, actor)).Items.SequenceEqual(prefix.Items.Concat(suffix.Items)) &&
            await EmptyTail(scope, actor, suffix.Next);

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

    private static bool Waiting(InstanceRow? row, Contract.Page<Contract.JournalEntry> prefix) =>
        row is { Status: InstanceStatus.Running } && prefix.Items.Count > 0 &&
        prefix.Items.All(entry => entry.Revision == row.Revision) &&
        prefix.Items.Any(entry => Json.Read<string>(entry.Entry) == "start");

    private static bool Completed(InstanceRow? row, InstanceRow? original, string output) =>
        row is { Status: InstanceStatus.Finished } && original is not null &&
        (row.Id, row.Creation, row.Revision) == (original.Id, original.Creation, original.Revision + 1) &&
        Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() == output;

    private static bool FinalJournal(InstanceRow? row, Contract.Page<Contract.JournalEntry> suffix)
    {
        if (row is null || suffix.Items.Any(entry => entry.Revision != row.Revision)) return false;
        var entries = suffix.Items.Select(entry => Json.Read<string>(entry.Entry))
            .Where(entry => entry == "end" || Completion(entry, "http") || Completion(entry, "mail")).ToArray();
        return entries is [var http, var mail, "end"] && Completion(http, "http") && Completion(mail, "mail");

        static bool Completion(string entry, string node) =>
            entry.StartsWith("c:", StringComparison.Ordinal) && entry.EndsWith(":" + node, StringComparison.Ordinal);
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
                HttpSendEffect { Request: var request } when request.Method == "POST" &&
                    request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) && !string.IsNullOrWhiteSpace(idempotencyKey) =>
                    (Channel.Http, idempotencyKey, request.Url, request.Body!.GetValue<string>()),
                HttpSendEffect => throw new InvalidOperationException("The invoice receiver requires a POST with a nonempty Idempotency-Key header."),
                EmailSendEffect { Mail: var mail } =>
                    (Channel.Mail, mail.MessageId, mail.To, mail.Body),
                _ => throw new InvalidOperationException("The fake receiver only accepts invoice POSTs and email.")
            };
            Applied.TryAdd((channel, key), payload);
            Arrivals.Add(new(owner, outgoing.Actor, channel, key, destination, payload));
        }
    }
}
