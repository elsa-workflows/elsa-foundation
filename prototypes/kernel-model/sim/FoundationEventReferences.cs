using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>Qualified event-created lives from both registration orders through real worker delivery.</summary>
internal static class FoundationEventReferences
{
    public static async Task Run(Action<bool, string> check)
    {
        const string tenant = "acme";
        const long now = 1_700_200_000_000;
        var store = new InMemoryStore(1, () => now);
        var spec = new Contract.StartSpec("event-refs", "start");
        var scope = new Contract.BoundScope(tenant);
        var ingress = new EventIngress(store, new Requests("event-reference-requests"), "caller");
        await Events.PublishAsync(store, new EventRow("acme_late", "published-first", "kept", now + 60_000) { FoundationTenant = "acme" });
        check(store.Messages.Count == 0, "a kept event remains without a creator before the start subscription");

        await ingress.SetStartAsync(scope, "late", "start", spec);
        await ingress.SetStartAsync(scope, "late", "start", spec);
        var late = store.Messages.Values.Single(row => row.Message.Start is not null).Message;
        var lateRef = store.ReferenceFor(tenant, new(late.Target.Id, 1));
        check(store.Messages.Count == 2 && late is { TargetCreation: 1, Start.Creation.Generation: 1 } &&
              store.AddFoundationMessage("other", lateRef, "foreign", "signal") is null &&
              store.AddFoundationMessage(tenant, lateRef, "exact-before-start", "signal", "follows-kept") == true,
            "the kept event creates one tenant-bound exact life and accepts an exact send before birth");

        await ingress.SetStartAsync(scope, "live", "start", spec);
        var liveEvent = new EventRow("acme_live", "registered-first", "live", now + 60_000);
        var accepted = store.PublishRequest(liveEvent with { FoundationTenant = "acme" }, "unused-reply");
        var target = accepted?.Single().Target ?? default;
        check(target.Id is not null && target != late.Target &&
              store.Messages.Values.Any(row => row.Message.Target == target && row.Message.Start?.Creation?.Generation == 1),
            "a request matching a live start subscription creates its own exact life");
        var liveRef = store.ReferenceFor(tenant, new(target.Id!, 1));

        Worker Owner(string node) => new(node, 0, store, new WorkflowInstance([new Probe()]),
            new WorkerOptions { Monotonic = () => 0 });
        var first = (await new DurableHost([Owner("event-ref-first")]).ActivateAsync(0))!;
        await Pump(first);
        check(store.ReadFoundationActor(tenant, lateRef).Actor?.Row.State == "[\"kept\",\"follows-kept\"]" &&
              store.ReadFoundationActor(tenant, liveRef).Actor?.Row.State == "[\"live\"]",
            "exact event-created actors run their first real workflow turn");
        await first.DisposeAsync();

        await using var successor = (await new DurableHost([Owner("event-ref-replacement")]).ActivateAsync(0))!;
        check(store.AddFoundationMessage(tenant, liveRef, "after-take", "signal", "after-take") == true,
            "the event-created reference survives owner replacement");
        await Pump(successor);
        check(store.ReadFoundationActor(tenant, liveRef).Actor?.Row.State == "[\"live\",\"after-take\"]",
            "the replacement routes one exact delivery to the original event-created actor");

        await ingress.SetStartAsync(scope, "live", "start", null);
        check(store.PublishRequest(new EventRow("acme_live", "after-unpublish", null, now) { FoundationTenant = "acme" }, "unused") is null,
            "removing the start subscription leaves no event-request waiter");

        await store.SubscribeAsync(new SubscriptionRow("acme_mixed-legacy", "", "start", new StartSpec("event-refs", "legacy")));
        await ingress.SetStartAsync(scope, "mixed-bound", "start", spec);
        var boundRejected = false;
        var legacyRejected = false;
        try { await ingress.SetStartAsync(scope, "mixed-legacy", "start", spec); }
        catch (StoreException exception) when (exception.Failure == Failure.Definite) { boundRejected = true; }
        try { await store.SubscribeAsync(new SubscriptionRow("acme_mixed-bound", "", "start", new StartSpec("event-refs", "legacy"))); }
        catch (StoreException exception) when (exception.Failure == Failure.Definite) { legacyRejected = true; }
        check(boundRejected && legacyRejected, "legacy and bound subscriptions cannot silently collide on a physical key");
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Model<List<string>>
    {
        public override string Key => "event-refs";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Payload is { } payload) state.Add(payload);
        }
    }
}
