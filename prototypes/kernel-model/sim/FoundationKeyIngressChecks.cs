using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationKeyIngressChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await ConcurrentStarts(StringComparer.OrdinalIgnoreCase, "other-op", "input", check);
        await ConcurrentStarts(StringComparer.OrdinalIgnoreCase, "first-op", "input", check);
        await ConcurrentStarts(StringComparer.OrdinalIgnoreCase, "first-op", "conflict", check);
        await ConcurrentStarts(StringComparer.Ordinal, "first-op", "input", check);

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

    private static async Task ConcurrentStarts(StringComparer equality, string secondOperation, string secondInput,
        Action<bool, string> check)
    {
        const int partitions = 1024;
        const long now = 1_700_700_000_000;
        var scope = new Contract.BoundScope("a");
        var store = new InMemoryStore(partitions, () => now, Flaw.None, StringComparer.Ordinal, equality);
        var attempts = new[] { (Key: "Invoice", Operation: "first-op", Input: "input"),
            (Key: "invoice", Operation: secondOperation, Input: secondInput) };
        var independentAddresses = attempts.Select(attempt =>
        {
            var name = scope.Tenant + "_" + attempt.Key;
            return InstanceRef.In(InstanceRef.Spread(name, partitions), name);
        }).ToArray();
        var ready = attempts.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = attempts.Select((attempt, index) => Task.Run(async () =>
        {
            var edge = new OrdinaryIngress(store);
            ready[index].SetResult();
            await release.Task;
            return await edge.SendAsync(scope,
                new Contract.Target.Key(attempt.Key, new Contract.StartSpec("key-ingress", "one", attempt.Input)),
                attempt.Operation, Contract.Kinds.Start);
        })).ToArray();
        await Task.WhenAll(ready.Select(arrived => arrived.Task));
        release.SetResult();
        var admissions = await Task.WhenAll(pending);
        var accepted = admissions.Select((receipt, index) => (receipt, index))
            .Where(entry => entry.receipt.Outcome == Contract.AdmissionOutcome.Accepted).ToArray();
        var folds = equality.Equals(attempts[0].Key, attempts[1].Key);
        var expected = folds ? 1 : 2;
        var creators = store.Messages.Values.Select(row => row.Message).Where(message => message.Kind == Kinds.Start).ToArray();
        var correctlyPlaced = independentAddresses[0].Partition != independentAddresses[1].Partition &&
            accepted.Length == expected && creators.Length == expected &&
            accepted.All(entry => creators.Any(message => message.Target == independentAddresses[entry.index] &&
                message.TargetCreation == 1 && store.ReferenceFor(scope.Tenant, new(message.Target.Id, 1)) == entry.receipt.Actor?.Id));
        check(correctlyPlaced, $"KB-1 {Label()} uses exactly {expected} winning spelling and placement pairs");
        if (!correctlyPlaced) return;

        if (folds)
        {
            var winner = accepted[0];
            var loser = admissions[1 - winner.index];
            var matchingOperation = secondOperation == attempts[0].Operation;
            var matchingRequest = secondInput == attempts[0].Input;
            check(matchingOperation && matchingRequest
                ? loser.Outcome == Contract.AdmissionOutcome.Duplicate && loser.Actor == winner.receipt.Actor
                : loser.Outcome == Contract.AdmissionOutcome.Refused && loser.Actor is null &&
                  (!matchingOperation ? loser.Reason == Contract.RefusalReason.Repeat : loser.Reason != Contract.RefusalReason.Repeat),
                $"KB-1 {Label()} returns the corresponding receipt for the losing request");
        }

        foreach (var creator in creators)
        {
            var worker = new Worker($"key-owner-{creator.Target.Partition}", creator.Target.Partition, store,
                new WorkflowInstance([new Probe()]), new WorkerOptions { Monotonic = () => 0, Partitions = partitions });
            await using var owner = (await new DurableHost([worker]).ActivateAsync(creator.Target.Partition))!;
            await Pump(owner);
        }
        check(store.Instances.Count == expected && store.Instances.Values.All(row => row.Creation == 1),
            $"KB-1 {Label()} commits exactly {expected} creation-one actor rows through their real owners");

        string Label() => folds ? $"folded ({secondOperation}, {secondInput})" : "ordinal control";
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
