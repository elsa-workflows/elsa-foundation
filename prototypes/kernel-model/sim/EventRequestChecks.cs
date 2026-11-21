using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>The in-memory admission witness for FC-C2.</summary>
internal static class EventRequestChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long now = 1_700_000_000_000;
        const string key = "acme_signal";
        var time = now;
        var store = new InMemoryStore(2, () => time);
        var request = new EventRow(key, "request", "original", now + 60_000);
        var spec = new StartSpec("event-probe", "event");
        var requests = new Requests("event-requests");
        var transport = new Replies(requests);
        var host = new DurableHost([Owner(0), Owner(1)]);
        Worker Owner(int partition) => new("event-owner-" + partition, partition, store,
            new WorkflowInstance([new EventProbe()]), new WorkerOptions { Monotonic = () => 0, Transport = transport, Partitions = 2 });
        await using var firstOwner = (await host.ActivateAsync(0))!;
        await using var secondOwner = (await host.ActivateAsync(1))!;

        async Task Pump()
        {
            for (var step = 0; step < 20; step++)
            {
                await firstOwner.WokenAsync(null);
                await secondOwner.WokenAsync(null);
                await firstOwner.RunAsync();
                await secondOwner.RunAsync();
                await Task.Yield();
            }
        }
        var otherTenant = new SubscriptionRow("other_signal", "", "foreign", spec);
        await Events.SubscribeAsync(store, otherTenant, 2);

        var missed = store.PublishRequest(request, "first");
        check(missed is null && store.EventRows.Count == 0 && store.Messages.Count == 0,
            "an event request without a matching wait writes neither event nor delivery");

        var starter = new SubscriptionRow(key, "", "start", spec);
        await Events.SubscribeAsync(store, starter, 2);
        check(store.Messages.Count == 0, "a later keeping start cannot catch up a refused event request");

        var started = store.PublishRequest(request, "retry");
        var first = started?.SingleOrDefault();
        check(first is { Added: true } && first.Target.EventStartAddress && store.Messages.Values.Single().Message.ReplyTo == "retry",
            "a start subscription alone counts as a waiter for the same event id on retry");

        var active = InstanceRef.In(1, "acme_waiter");
        await store.AddAsync(new Message("arm", active, "arm", Json.Write(key),
            Start: spec with { Creation = new(1, 0, "event-waiter") }, TargetCreation: 1));
        await Pump();
        check(store.Subscriptions.Any(wait => wait.Instance == active.Id && wait.Creation == 1),
            "a workflow commits the qualified active wait");

        var fanout = request with { Id = "fanout" };
        var caller = requests.Open("fanout", now + 60_000);
        var address = caller.ReplyAddress("caller");
        var published = store.PublishRequest(fanout, address);
        var rows = store.Messages.Values.Where(row => row.Message.ReplyTo == address).ToArray();
        check(published is { Count: 2 } && rows is { Length: 2 } &&
              rows.Any(row => row.Message.Target == active && row.Message.TargetCreation == 1) &&
              rows.Any(row => row.Message.Target.EventStartAddress && row.Message.Start == spec),
            "an admitted request attaches its reply route to every matched start and qualified active wait");

        var duplicate = store.PublishRequest(fanout with { Payload = "changed" }, "duplicate");
        check(duplicate is { Count: 0 } && store.Messages.Count == 3 &&
              store.EventRows[(key, "fanout")].Payload == "original",
            "repeating an accepted event id preserves its payload and inserts no second matched delivery");

        var kept = request with { Key = "acme_keep", Id = "publication" };
        await Events.PublishAsync(store, kept, 2);
        await Events.SubscribeAsync(store, starter with { Key = kept.Key, Wait = "late" }, 2);
        check(store.Messages.Count == 4 && store.EventRows.ContainsKey((kept.Key, kept.Id)),
            "an ordinary kept publication still reaches a later registration without a request");

        await Pump();
        check(rows.All(delivery => store.Instances.TryGetValue(delivery.Message.Target.Id, out var row) &&
                Json.Read<List<string>>(row.State).SequenceEqual(["original"])),
            "both the active actor and the new actor commit the event through the real owners");
        check(caller is { Responses: 1, Answer: "original" } && transport.Sent == 2,
            "the first committed answer wins the request even though both actors answer");

        var lateWaiter = InstanceRef.In(1, "acme_late_waiter");
        await store.AddAsync(new Message("arm-late", lateWaiter, "arm", Json.Write(key),
            Start: spec with { Creation = new(1, 0, "late-waiter") }, TargetCreation: 1));
        await Pump();
        check(store.Subscriptions.Any(wait => wait.Instance == lateWaiter.Id && wait.Creation == 1),
            "a subsequent non-keeping wait is committed after the first publication");
        var laterDuplicate = store.PublishRequest(fanout, "duplicate-after-registration");
        await Pump();
        check(laterDuplicate is { Count: 0 } && Json.Read<List<string>>(store.Instances[lateWaiter.Id].State).Count == 0 &&
              store.Messages.Values.All(row => row.Message.Target != lateWaiter || Events.Delivered(row.Message)?.Event != fanout.Id),
            "repeating the event id cannot deliver the old publication to a new non-keeping wait");

        var bound = new EventRequests(store, requests, "caller");
        var scope = new Contract.BoundScope("acme");
        var eventTarget = new Contract.Target.Event("typed");
        var keep = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1));
        var options = new Contract.RequestOptions(null, now + 60_000);
        await Events.SubscribeAsync(store, starter with { Key = "other_typed" }, 2);
        var missedTyped = bound.RequestAsync(scope, eventTarget, "typed-request", "typed", keep, options);
        check(missedTyped.IsCompletedSuccessfully &&
              (await missedTyped) is { Error: "no waiter", Status: null } &&
              !store.FoundationEvents.ContainsKey(("acme_typed", "typed-request")) && requests.All.Count == 0,
            "the bound request returns no waiter immediately, publishes nothing and releases the caller");

        await Events.SubscribeAsync(store, starter with { Key = "acme_typed" }, 2);
        var sameSpelling = await bound.RequestAsync(scope, eventTarget, "typed-request", "typed", null, options);
        check(sameSpelling.Error == "no waiter" && !store.FoundationEvents.ContainsKey(("acme_typed", "typed-request")),
            "an unbound wait with the same spelling cannot receive a tenant-bound request");
        await bound.SetStartAsync(scope, "typed", "scoped", new Contract.StartSpec("event-probe", "event"));
        var boundAnswer = bound.RequestAsync(scope, eventTarget, "typed-request", "typed", null, options).AsTask();
        await Pump();
        check((await boundAnswer.WaitAsync(TimeSpan.FromSeconds(2))) is { Channel: "event-output", Item: "typed" },
            "the same bound request id can be retried and accept the actor's first output channel");

        var timed = bound.RequestAsync(scope, eventTarget, "deadline", "typed", null, options with { Deadline = now + 10 }).AsTask();
        time += 10;
        check((await timed.WaitAsync(TimeSpan.FromSeconds(2))) is { Error: "timeout" } && requests.All.Count == 0,
            "a bound event request releases its reply registration at the shared clock deadline");

        using var cancellation = new CancellationTokenSource();
        var cancelled = bound.RequestAsync(scope, eventTarget, "cancelled-caller", "typed", keep, options, cancellation.Token).AsTask();
        cancellation.Cancel();
        var cancelledWait = false;
        try { await cancelled.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelledWait = true; }
        check(cancelledWait && requests.All.Count == 0 &&
              store.Messages.Values.Any(row => Events.Delivered(row.Message) is { Event: "cancelled-caller" }),
            "cancelling the caller closes its registration and keeps the admitted delivery");

        await store.SweepAsync();
        check(!store.FoundationEvents.ContainsKey(("acme_typed", "typed-request")) &&
              store.FoundationEvents.ContainsKey(("acme_typed", "cancelled-caller")),
            "sweep removes an admitted event without a keep window and preserves one still kept");
    }

    public static async Task Provenance(Action<bool, string> check)
    {
        const long now = 1_700_000_000_000;
        const string key = "acme_signal";
        var store = new InMemoryStore(1, () => now);
        var start = new StartSpec("event-probe", "probe");
        var old = new SubscriptionRow(key, "", "start", start);
        var publication = new EventRow(key, "same-id", "legacy", now + 60_000);
        await Events.SubscribeAsync(store, old, 1);
        var legacy = await store.AddEventAsync(new EventRoute(publication, old).Delivery());
        store.Subscriptions.Remove(old);
        store.SetFoundationEventStart("acme", "signal", "start", start);
        var bound = store.Subscriptions.Single();

        var mismatched = false;
        try { _ = new EventRoute(publication, bound).Delivery(); }
        catch (ArgumentException) { mismatched = true; }

        check((await store.FindSubscriptionsAsync(key)).Count == 0 && mismatched,
            "the legacy discovery and direct route cannot borrow a bound start subscription's authority");
        var request = publication with { FoundationTenant = "acme", Payload = "tenant" };
        var receipt = store.PublishRequest(request, "local-registration");
        check(receipt is [{ Added: true, Target: var started }] && started != legacy.Target &&
            store.Messages.Values.Any(row => row.Message.Target == started && row.Message.Payload == "tenant") &&
            store.Messages.Values.Any(row => row.Message.Target == legacy.Target && row.Message.Payload == "legacy"),
            "a retained legacy match cannot suppress a scoped delivery of the same key, id and wait");
    }

    private sealed class EventProbe : Model<List<string>>
    {
        public override string Key => "event-probe";

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "arm") turn.Subscribe(message.Read<string>(), "active");
                if (message.Kind != Kinds.Event || message.Payload is not { } payload) continue;
                state.Add(payload);
                turn.Reply(message, payload == "typed" ? "event-output" : Request.Response, payload);
            }
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public int Sent { get; private set; }

        public void Send(string node, Reply reply)
        {
            Sent++;
            requests.Receive(reply);
        }
    }
}
