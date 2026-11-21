using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationKeyIngressChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_700_700_000_000;
        var now = initial;
        var store = new InMemoryStore(2, () => now);
        var scope = new Contract.BoundScope("acme");
        var other = new Contract.BoundScope("other");
        var edge = new OrdinaryIngress(store);
        var options = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(40));
        var spec = new Contract.StartSpec("key-ingress", "one", "input");
        var first = await edge.SendAsync(scope, new Contract.Target.Key("Invoice", spec), "op-1", Contract.Kinds.Start, options: options);
        var retry = await edge.SendAsync(scope, new Contract.Target.Key("INVOICE", spec), "op-1", Contract.Kinds.Start, options: options);
        var foreign = await edge.SendAsync(other, new Contract.Target.Key("invoice", spec), "op-1", Contract.Kinds.Start, options: options);
        var conflict = await edge.SendAsync(scope, new Contract.Target.Key("invoice", spec with { Input = "different" }), "op-1", Contract.Kinds.Start, options: options);
        var upper = await edge.SendAsync(scope, new Contract.Target.Key("invoice", spec), "OP-1", Contract.Kinds.Start, options: options);
        var reference = first.Actor!.Value;
        var oldAddress = store.Messages.Values.Single(row => row.Message.Start?.Creation is not null &&
            store.ReferenceFor(scope.Tenant, new(row.Message.Target.Id, 1)) == reference.Id).Message.Target;
        var equivalent = first.Outcome == Contract.AdmissionOutcome.Accepted &&
            retry is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var same } && same == reference &&
            foreign.Actor != reference && conflict is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            upper is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat };
        check(equivalent, "equivalent key spellings share one start receipt, with distinct tenants and exact operation tokens");
        if (!equivalent) return;

        var key = new Contract.Target.Key("invoice");
        var firstCommand = await edge.SendAsync(scope, key, "cmd-1", "note", "one");
        var firstRetry = await edge.SendAsync(scope, new Contract.Target.Key("INVOICE"), "cmd-1", "note", "one");
        var second = await edge.SendAsync(scope, key, "CMD-1", "note", "two");
        var malformed = await edge.SendAsync(scope, key, "nul\0\uD800", "note", "three");
        var wire = store.Messages.Values.Where(row => row.Message.Target == oldAddress && row.Message.Kind == "note")
            .Select(row => row.Message.Id).ToArray();
        var forgedId = await edge.SendAsync(scope, new Contract.Target.Ref(reference), wire[0], "note", "forged");
        check(firstCommand.Actor == first.Actor && firstRetry.Outcome == Contract.AdmissionOutcome.Duplicate &&
            second.Outcome == Contract.AdmissionOutcome.Accepted && malformed.Outcome == Contract.AdmissionOutcome.Accepted &&
            wire.Length == 3 && wire.All(id => id.StartsWith("key/", StringComparison.Ordinal) && id.Length == 68) &&
            forgedId.Outcome == Contract.AdmissionOutcome.Refused,
            "keyed sends pin the exact life and keep operation code units out of stored messages");

        async Task<IScopeActivation> Owner(string node, int partition)
        {
            var worker = new Worker(node, partition, store, new WorkflowInstance([new Probe()]),
                new WorkerOptions { Monotonic = () => now - initial, Partitions = 2 });
            return (await new DurableHost([worker]).ActivateAsync(partition))!;
        }
        await using var owner = await Owner("key-ingress", oldAddress.Partition);
        await Pump(owner);
        check(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor is { Row.State: "[\"input\",\"one\",\"two\",\"three\"]" },
            "the receiving Worker applies the keyed attempts once and in acceptance order");

        await edge.SendAsync(scope, key, "finish", "finish");
        await Pump(owner);
        check(store.AddFoundationMessage(scope.Tenant, reference.Id, "trusted-retirement", Kinds.Delete) == true,
            "the ended keyed fixture admitted its trusted retirement command");
        await Pump(owner);
        var tooSoon = await edge.SendAsync(scope, new Contract.Target.Key("iNvOiCe", spec), "op-2", Contract.Kinds.Start, options: options);
        check(tooSoon is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat },
            "the original key's reuse window protects the retired run");

        now += 41;
        await store.SweepAsync();
        var recreated = await edge.SendAsync(scope, new Contract.Target.Key("iNvOiCe", spec), "op-2", Contract.Kinds.Start, options: options);
        await Pump(owner);
        var replay = await edge.SendAsync(scope, new Contract.Target.Key("INVOICE"), "cmd-1", "note", "one");
        check(recreated is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } next } && next != reference &&
            store.ReadFoundationActor(scope.Tenant, next.Id).Actor is { Row: { Creation: 2, State: "[\"input\"]" } row } &&
            row.Id == oldAddress.Id && replay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var old } && old == reference,
            "recreating a key keeps its original placement and an old admitted operation cannot reach the successor");
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
        public override string Key => "key-ingress";

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Payload is { } payload) state.Add(payload);
                if (message.Kind == "finish") turn.SetStatus(InstanceStatus.Finished);
            }
        }
    }
}
