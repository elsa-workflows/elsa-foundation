using System.Text.Json;
using Kernel.Durable;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

internal static class PublicOrderExample
{
    internal static async Task RunAsync(bool omitStockRelease)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            await RunAsync(mode, accepted: true, omitStockRelease);
            await RunAsync(mode, accepted: false, omitStockRelease);
        }
    }

    private static async Task RunAsync(string mode, bool accepted, bool omitStockRelease)
    {
        const long start = 1_705_000_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("orders");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = budget.Token;
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("orders-ingress"), "orders-ingress");
        Contract.IActorReads reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Definition(omitStockRelease));
        using var work = new StringWriter();
        var previousOutput = WriteLine.Out;
        WriteLine.Out = work;
        try
        {
            var birth = await runtime.StartAsync(scope, "order-start", NativeExecutionModel.Start("order", mode), cancellationToken: token);
            Require(birth is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The order must be admitted.");
            var actor = birth.Actor!.Value;
            string frame;
            await using (var first = await ActivateAsync("order-owner"))
            {
                await DriveAsync(first);
                frame = (await HistoryAsync(actor)).Single(entry => entry is [_, null, "entry", "AwaitingPayment"])[0]!;
                Require(await GetStatusAsync(actor) == Contract.ActorStatus.Running && Marks().SequenceEqual(["reserve stock", "book shipment"]),
                    "The order must wait for payment after executing stock and shipping activities.");
            }

            now++;
            await using var replacement = await ActivateAsync("order-replacement");
            var decision = accepted ? "approved" : "declined";
            var sent = await SignalAsync(actor, frame, "payment-decision", decision);
            Require(sent.Outcome == Contract.AdmissionOutcome.Accepted, "The payment decision must be admitted after owner replacement.");
            await DriveAsync(replacement);
            var replay = await SignalAsync(actor, frame, "payment-decision", decision);
            Require(replay.Outcome == Contract.AdmissionOutcome.Duplicate, "The payment operation must keep its receipt across retry.");
            await DriveAsync(replacement);

            if (!accepted)
            {
                Require(await GetStatusAsync(actor) == Contract.ActorStatus.Running &&
                    (await HistoryAsync(actor)).Count(entry => entry is [_, _, "entry", "ReleasingShipment"]) == 1 &&
                    Marks().SequenceEqual(["reserve stock", "book shipment", "release shipment"]),
                    "Releasing shipment must wait for its acknowledgement before releasing stock.");
                var confirmation = await SignalAsync(actor, frame, "shipment-release-confirmed", "shipment-released");
                Require(confirmation.Outcome == Contract.AdmissionOutcome.Accepted, "The shipment release acknowledgement must be admitted.");
                await DriveAsync(replacement);
                Require(await GetStatusAsync(actor) == Contract.ActorStatus.Running,
                    "Releasing stock must wait for its acknowledgement before closing the order.");
                Require(Marks().SequenceEqual(["reserve stock", "book shipment", "release shipment", "release stock"]),
                    "The ordinary stock release activity must run before its acknowledgement.");
                var closed = await SignalAsync(actor, frame, "stock-release-confirmed", "stock-released");
                Require(closed.Outcome == Contract.AdmissionOutcome.Accepted, "The stock release acknowledgement must be admitted.");
                await DriveAsync(replacement);
            }

            var history = await HistoryAsync(actor);
            var states = history.Where(entry => entry[2] == "entry").Select(entry => entry[3]);
            var expectedStates = accepted
                ? new[] { "AwaitingPayment", "Fulfilled" }
                : new[] { "AwaitingPayment", "ReleasingShipment", "ReleasingStock", "Reversed" };
            var expectedWork = accepted
                ? new[] { "reserve stock", "book shipment", "fulfill order" }
                : new[] { "reserve stock", "book shipment", "release shipment", "release stock" };
            Require(await GetStatusAsync(actor) == Contract.ActorStatus.Finished && states.SequenceEqual(expectedStates),
                "The public journal must show one ordered statechart path and a finished actor.");
            Require(Marks().SequenceEqual(expectedWork), "The order must run every ordinary activity once and in the expected order.");
            Console.WriteLine($"{mode} {decision}: {string.Join(" -> ", expectedStates)}. Activities: {string.Join(", ", expectedWork)}");
        }
        finally
        {
            WriteLine.Out = previousOutput;
        }

        string[] Marks() => work.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        async Task<Contract.ActorStatus?> GetStatusAsync(Contract.ActorRef actor) =>
            (await reads.GetAsync(scope, actor, token))?.Header.Status;

        ValueTask<Contract.Admission> SignalAsync(Contract.ActorRef actor, string frame, string operation, string trigger) =>
            ingress.SendAsync(scope, new Contract.Target.Ref(actor), operation, CompositeSignal.Kind,
                JsonSerializer.Serialize(new CompositeSignal(frame, trigger)), cancellationToken: token);

        async Task<IScopeActivation> ActivateAsync(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)])),
                new WorkerOptions { Monotonic = () => now - start });
            return await new DurableHost([worker]).ActivateAsync(0, token)
                ?? throw new InvalidOperationException("The Embedded owner could not activate.");
        }

        async Task DriveAsync(IScopeActivation owner)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                await owner.WokenAsync(null, token);
                await owner.RunAsync(token);
                await Task.Yield();
            }
        }

        async Task<List<string?[]>> HistoryAsync(Contract.ActorRef actor)
        {
            var entries = new List<string?[]>();
            Contract.PageToken? bookmark = null;
            while (true)
            {
                var page = await reads.TailAsync(scope, actor, bookmark, token);
                foreach (var row in page.Items)
                {
                    using var json = JsonDocument.Parse(row.Entry);
                    if (json.RootElement.ValueKind == JsonValueKind.String &&
                        json.RootElement.GetString() is { } item && item.StartsWith("statechart:", StringComparison.Ordinal))
                        entries.Add(JsonSerializer.Deserialize<string?[]>(item["statechart:".Length..])!);
                }
                if (page.Next is not { } next) return entries;
                if (next == bookmark) throw new InvalidOperationException("The journal cursor stopped advancing.");
                bookmark = next;
            }
        }
    }

    private static Workflow Definition(bool omitStockRelease)
    {
        var chart = new StructuralDefinition(
            [new("Order", NodeKind.Root, Initial: "AwaitingPayment"),
             new("AwaitingPayment", NodeKind.Atomic, "Order", Entry: "prepare"),
             new("Fulfilled", NodeKind.Final, "Order", Entry: "fulfill"),
             new("ReleasingShipment", NodeKind.Atomic, "Order", Entry: "reverse-shipment"),
             new("ReleasingStock", NodeKind.Atomic, "Order", Entry: omitStockRelease ? null : "release-stock"),
             new("Reversed", NodeKind.Final, "Order")],
            [new("approved", 0, "AwaitingPayment", "Fulfilled", VisitMode.Reenter, "approved"),
             new("declined", 1, "AwaitingPayment", "ReleasingShipment", VisitMode.Reenter, "declined"),
             new("shipment-released", 2, "ReleasingShipment", "ReleasingStock", VisitMode.Reenter, "shipment-released"),
             new("stock-released", 3, "ReleasingStock", "Reversed", VisitMode.Reenter, "stock-released")]);
        var machine = Wf.Node<StateMachine>("order-machine", ("StructuralDefinition", Binding.Literal(chart)))
            .With(Wf.Sequence("prepare", Wf.WriteLine("reserve-stock", Binding.Literal("reserve stock")),
                    Wf.WriteLine("book-shipment", Binding.Literal("book shipment"))),
                Wf.WriteLine("fulfill", Binding.Literal("fulfill order")),
                Wf.WriteLine("reverse-shipment", Binding.Literal("release shipment")),
                Wf.WriteLine("release-stock", Binding.Literal("release stock")));
        return Wf.Workflow("order", machine);
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
