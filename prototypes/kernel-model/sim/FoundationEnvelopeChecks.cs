using System.Globalization;
using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationEnvelopeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long now = 1_701_300_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("turn-cap"), "turn-cap");
        Contract.IActorReads reads = new ActorList(store, 2);
        var model = new Probe();
        var worker = new Worker("turn-cap", 0, store, new ActorAdapter(store, model), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var atCap = await Admit("at-cap", 10_000.ToString(CultureInfo.InvariantCulture));
        var overCap = await Admit("over-cap", 10_001.ToString(CultureInfo.InvariantCulture));
        var labelsAtCap = await Admit("labels-at-cap", "labels-at-cap");
        var invalidLabels = new[] { "label-count", "label-name", "label-value" };
        var refused = new List<(string Case, Contract.ActorRef Ref)>();
        foreach (var scenario in invalidLabels)
            refused.Add((scenario, await Admit(scenario, scenario)));
        var metadataAtCap = await Admit("meta-fit", "meta-fit");
        var metadataAboveCap = await Admit("meta-over", "meta-over");
        for (var step = 0; step < 12; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }

        var accepted = store.ReadFoundationActor(scope.Tenant, atCap.Id).Actor;
        var failed = store.ReadFoundationActor(scope.Tenant, overCap.Id).Actor;
        check(accepted is { Row: { Status: InstanceStatus.Running, State: "10000" } } &&
            store.Messages.Values.Count(row => row.Message.Target.Id == accepted.Row.Id && row.Message.Kind == "own-timer") == 10_000,
            "the public actor can commit a turn with exactly 10,000 staged sends");
        check(failed is { Row.State: "null" } &&
            (await reads.GetAsync(scope, overCap)) is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } &&
            !store.Messages.Values.Any(row => row.Message.Target.Id == failed.Row.Id && row.Message.Kind == "own-timer"),
            "one send above the bound parks the actor and rolls all staged effects back");

        var acceptedLabels = await reads.GetAsync(scope, labelsAtCap);
        check(acceptedLabels is { Header: { Status: Contract.ActorStatus.Running, Labels.Count: 4 } } &&
            acceptedLabels.Header.Labels.All(pair => System.Text.Encoding.UTF8.GetByteCount(pair.Key) == 1_024 &&
                System.Text.Encoding.UTF8.GetByteCount(pair.Value) == 1_024),
            "four labels with 1,024 UTF-8 bytes per name and value commit through the public actor turn");
        foreach (var (scenario, reference) in refused)
        {
            var view = await reads.GetAsync(scope, reference);
            var row = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
            check(view is { Park: not null, Header: { Status: Contract.ActorStatus.Faulted, Labels.Count: 0 } } &&
                row.State == "null" &&
                !store.Messages.Values.Any(item => item.Message.Target.Id == row.Id && item.Message.Kind == "do-not-commit"),
                $"an excess {scenario} parks the actor and rolls back its header, checkpoint and staged send");
        }

        var validMetadata = store.ReadFoundationActor(scope.Tenant, metadataAtCap.Id).Actor!.Row;
        var invalidMetadata = store.ReadFoundationActor(scope.Tenant, metadataAboveCap.Id).Actor!;
        var kindAtCap = new string('é', 128);
        var nameAtCap = new string('é', 512);
        check(store.Messages.Values.Any(item => item.Message.Target.Id == validMetadata.Id && item.Message.Kind == kindAtCap) &&
            invalidMetadata.Park is not null && !store.Messages.Values.Any(item =>
                item.Message.Target.Id == invalidMetadata.Row.Id && item.Message.Kind == "before-metadata"),
            "an authored send accepts 256 kind bytes and one extra byte rolls back the turn");

        var target = new Contract.Target.Ref(metadataAtCap);
        var beforeRefusals = store.Messages.Count;
        var badId = await ingress.SendAsync(scope, target, nameAtCap + "x", "ok");
        var badKind = await ingress.SendAsync(scope, target, "same-id", kindAtCap + "x");
        var invalidKind = await ingress.SendAsync(scope, target, "same-id", "broken\ud800");
        check(badId.Outcome == Contract.AdmissionOutcome.Refused && badKind.Outcome == Contract.AdmissionOutcome.Refused &&
            invalidKind.Outcome == Contract.AdmissionOutcome.Refused && store.Messages.Count == beforeRefusals,
            "ordinary ingress refuses names and kinds outside their text budgets before reserving a delivery");
        var atLimit = await ingress.SendAsync(scope, target, nameAtCap, kindAtCap);
        var retryKind = await ingress.SendAsync(scope, target, "same-id", "ok");
        check(atLimit.Outcome == Contract.AdmissionOutcome.Accepted && retryKind.Outcome == Contract.AdmissionOutcome.Accepted,
            "ordinary ingress accepts exact text budgets and reuses an ID after validation refused the first input");

        var specAtLimit = new Contract.StartSpec(model.Type, "probe", "0");
        var badKey = await ingress.SendAsync(scope, new Contract.Target.Key(nameAtCap + "x", specAtLimit), "key-op", Contract.Kinds.Start);
        var badOperation = await ingress.SendAsync(scope, new Contract.Target.Key(nameAtCap, specAtLimit), nameAtCap + "x", Contract.Kinds.Start);
        var goodKey = await ingress.SendAsync(scope, new Contract.Target.Key(nameAtCap, specAtLimit), "key-op", Contract.Kinds.Start);
        check(badKey.Outcome == Contract.AdmissionOutcome.Refused && badOperation.Outcome == Contract.AdmissionOutcome.Refused &&
            goodKey.Outcome == Contract.AdmissionOutcome.Accepted,
            "keyed admission enforces both name bounds before it creates a binding or receipt");

        async Task<Contract.ActorRef> Admit(string id, string input)
        {
            var start = new Contract.StartSpec(model.Type, "probe", input);
            return (await ingress.SendAsync(scope, new Contract.Target.Start(start), id, Contract.Kinds.Start)).Actor!.Value;
        }
    }

    public static async Task TerminalReserve(Action<bool, string> check)
    {
        const long now = 1_701_700_000_000;
        const string typeData = "envelope";
        const int growth = 128;
        const int cap = 16_457_728;
        const int headerReserve = 16_384;
        const int maxTypeData = 16_384;
        const int maxLabelBytes = 8_192;
        var maxState = cap - headerReserve - maxTypeData - maxLabelBytes - TerminalProbe.ModelType.Length - typeData.Length - growth;
        var fullHeader = new string('é', maxTypeData / 2);
        var oversizedHeader = fullHeader + "x";
        var model = new TerminalProbe(maxState, growth, fullHeader);
        var maxType = new string('é', 512);
        var hugeType = new string('t', 16_800_000);
        var hugeNamedActor = new TerminalProbe(maxState, growth, fullHeader, hugeType);
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("terminal-reserve"), "terminal-reserve",
            (bound, kind) => bound == scope && kind == Contract.Kinds.Terminate);
        Contract.IActorReads reads = new ActorList(store, 16);
        var router = new ActorTypeRouter(new ActorAdapter(store, model), new Dictionary<string, IActorType>
        {
            [maxType] = new ActorAdapter(store, new TerminalProbe(maxState, growth, fullHeader, maxType)),
            [hugeType] = new ActorAdapter(store, hugeNamedActor)
        });
        var worker = new Worker("terminal-reserve", 0, store, router, new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var fit = await Start("fits");
        var overflow = await Start("overflow");
        var excessive = await Start("overstated");
        var existing = await Start("existing");
        var fault = await Start("fault");
        var jobAtCap = await Start("key-fit");
        var jobAboveCap = await Start("key-over");
        var hugeJob = await Start("key-huge");
        var updateOver = await Start("header-update-over");
        var childOver = await Start("header-child-over");
        var admissible = new Contract.StartSpec(model.Type, fullHeader, "header-fit");
        var oversized = admissible with { TypeData = oversizedHeader };
        var legacyStart = store.AdmitGeneratedFoundationStart(scope.Tenant, "legacy-oversized",
            new StartSpec(model.Type, new string('x', cap + 1), "must-not-run"), null, null);
        var legacyNamedStart = store.AdmitGeneratedFoundationStart(scope.Tenant, "legacy-type",
            new StartSpec(hugeType, typeData, "must-not-run"), null, null);
        var badType = new Contract.Target.Start(new(hugeType, typeData, "must-not-run"));
        var typeRefusal = await ingress.SendAsync(scope, badType, "retry-type", Contract.Kinds.Start);
        var overType = new Contract.Target.Start(new(maxType + "x", typeData, "small"));
        var overTypeRefusal = await ingress.SendAsync(scope, overType, "type-over", Contract.Kinds.Start);
        var atType = new Contract.Target.Start(new(maxType, typeData, "small"));
        var typeFit = await ingress.SendAsync(scope, atType, "retry-type", Contract.Kinds.Start);
        var rootRefusal = await ingress.SendAsync(scope, new Contract.Target.Start(oversized), "retry-root", Contract.Kinds.Start);
        var keyedRefusal = await ingress.SendAsync(scope, new Contract.Target.Key("retry-key", oversized), "retry-key", Contract.Kinds.Start);
        var rootFit = await ingress.SendAsync(scope, new Contract.Target.Start(admissible), "retry-root", Contract.Kinds.Start);
        var keyedFit = await ingress.SendAsync(scope, new Contract.Target.Key("retry-key", admissible), "retry-key", Contract.Kinds.Start);
        var invalidEvent = new Contract.Target.Event("invalid-start-data");
        var eventRefused = false;
        try
        {
            await ingress.SetStartAsync(scope, invalidEvent.CorrelationKey, "invalid", oversized);
        }
        catch (ArgumentException)
        {
            eventRefused = true;
        }
        await ingress.SendAsync(scope, invalidEvent, "never-start", "publish");
        var validEvent = new Contract.Target.Event("valid-start-data");
        await ingress.SetStartAsync(scope, validEvent.CorrelationKey, "valid", admissible);
        await ingress.SendAsync(scope, validEvent, "start-event", "publish");
        await Pump();

        var fitted = await reads.GetAsync(scope, fit);
        check(fitted is { Header.Status: Contract.ActorStatus.Running, Park: null } && Row(fit).State.Length == maxState,
            "a live checkpoint and its declared terminal growth can fill the in-memory envelope exactly");
        foreach (var rejected in new[] { overflow, excessive })
        {
            var row = Row(rejected);
            check((await reads.GetAsync(scope, rejected)) is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } &&
                row.State == "null" && store.Messages.Values.All(item => item.Message.Target.Id != row.Id || item.Message.Kind != "speculative"),
                rejected == overflow ? "one byte of excess terminal growth parks without committing its state or timer" :
                    "an excessive growth bound parks without integer overflow or staged effects");
        }

        var before = Row(existing);
        await ingress.SendAsync(scope, new Contract.Target.Ref(existing), "grow", "grow");
        await Pump();
        check((await reads.GetAsync(scope, existing)) is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
            Row(existing).State == before.State && Row(existing).Revision == before.Revision + 1 &&
            store.Messages.Values.All(item => item.Message.Target.Id != before.Id || item.Message.Kind != "growth-leak"),
            "an over-limit update preserves the existing checkpoint and parks without its staged effects");

        var beforeFault = Row(fault);
        await ingress.SendAsync(scope, new Contract.Target.Ref(fault), "large-fault", "large-fault");
        await Pump();
        var parked = Row(fault);
        check(parked.State == beforeFault.State && parked.Status == InstanceStatus.Faulted &&
            parked.Fault is { } error && error[0] == 'é' && error.EndsWith('…') && System.Text.Encoding.UTF8.GetByteCount(error) <= 4_096,
            "a large multibyte fault preserves the last checkpoint with a bounded diagnostic");

        check(Row(jobAtCap) is { Status: InstanceStatus.Running, State: "key-fit" } && model.LegalJobEntered,
            "a job key at the 1,024-byte admission bound can run");
        foreach (var rejected in new[] { jobAboveCap, hugeJob })
        {
            var row = Row(rejected);
            check(row is { Status: InstanceStatus.Faulted, State: "null", Jobs: null } &&
                store.Messages.Values.All(item => item.Message.Target.Id != row.Id || item.Message.Kind != "speculative"),
                rejected == jobAboveCap ? "one excess job-key byte parks and rolls back staged effects" :
                    "a huge job key cannot enter the published checkpoint or the background driver");
        }

        check(typeRefusal is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            overTypeRefusal is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            typeFit is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } named } &&
            (await reads.GetAsync(scope, named)) is { Header: { Status: Contract.ActorStatus.Running, Type: var type }, Park: null } &&
            type == maxType,
            "oversized registered type names reserve no id and a 1,024-byte type can start on the same id");
        check(await reads.GetAsync(scope, new(legacyNamedStart.Reference)) is null && !hugeNamedActor.LegacyStartEntered &&
            store.Messages.Values.All(item => item.Message.Id != "legacy-type"),
            "an inherited oversized type cannot execute or enter the Park checkpoint");
        check(rootRefusal is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            rootFit is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } root } &&
            (await reads.GetAsync(scope, root))?.Header.TypeData == fullHeader,
            "an oversized generated start reserves no id and a 16 KiB retry is accepted");
        check(keyedRefusal is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            keyedFit is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } keyed } &&
            (await reads.GetAsync(scope, keyed))?.Header.TypeData == fullHeader,
            "an oversized keyed start reserves no key or id and a 16 KiB retry is accepted");
        check(await reads.GetAsync(scope, new(legacyStart.Reference)) is null && !model.LegacyStartEntered &&
            store.Messages.Values.All(item => item.Message.Id != "legacy-oversized"),
            "an inherited oversized pending start cannot execute, create a row or copy its header into Park");
        var visible = (await reads.ListAsync(scope, new())).Items;
        check(eventRefused && visible.All(item => item.Header.TypeData != oversizedHeader) &&
            visible.Count(item => item.Header.TypeData == fullHeader && item.Park is null) == 3,
            "event-start admission rejects one byte over and creates one actor from the at-cap registration");
        foreach (var rejected in new[] { updateOver, childOver })
        {
            var row = Row(rejected);
            var view = await reads.GetAsync(scope, rejected);
            check(view is { Park: not null } && view.Header.TypeData == typeData && row.State == "null" &&
                visible.All(item => item.Header.Parent != rejected) &&
                store.Messages.Values.All(item => item.Message.Target.Id != row.Id || item.Message.Kind != "speculative"),
                rejected == updateOver ? "oversized SetHeader rolls back the actor turn" :
                    "an oversized child spec rolls back the parent turn and writes no child");
        }

        var selection = new Contract.OperatorCommand(fitted!.Selection!);
        var terminate = await ingress.SendAsync(scope, new Contract.Target.Ref(fit), "terminate", Contract.Kinds.Terminate, Json.Write(selection));
        await Pump();
        check(terminate.Outcome == Contract.AdmissionOutcome.Accepted &&
            (await reads.GetAsync(scope, fit)) is { Header: { Status: Contract.ActorStatus.Cancelled } terminal, Park: null } &&
            terminal.TypeData == fullHeader && terminal.Labels.Count == 4 &&
            terminal.Labels.Sum(label => Encoding.UTF8.GetByteCount(label.Key) + Encoding.UTF8.GetByteCount(label.Value)) == maxLabelBytes &&
            Row(fit).State.Length == maxState + growth,
            "the exact-fit actor terminates with maximum state growth, 16 KiB type data and four maximum-sized labels");

        async Task<Contract.ActorRef> Start(string action) =>
            (await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, typeData, action)),
                action, Contract.Kinds.Start)).Actor!.Value;

        InstanceRow Row(Contract.ActorRef reference) => store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;

        async Task Pump()
        {
            for (var step = 0; step < 16; step++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }
    }

    private sealed class TerminalProbe(int atCap, int growth, string fullHeader, string type = TerminalProbe.ModelType) : Contract.IActorType
    {
        public const string ModelType = "terminal-probe";
        public string Type => type;
        public bool LegalJobEntered { get; private set; }
        public bool LegacyStartEntered { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when message.Payload is "key-fit" or "key-over" or "key-huge":
                        turn.State = message.Payload;
                        turn.Send(turn.Header.Ref, "speculative", dueAt: turn.Now + 1);
                        var key = message.Payload switch
                        {
                            "key-fit" => new string('é', 512),
                            "key-over" => new string('é', 512) + "x",
                            _ => new string('x', 40_000)
                        };
                        turn.StartJob(key);
                        break;
                    case Contract.Kinds.Start when message.Payload == "must-not-run":
                        LegacyStartEntered = true;
                        throw new InvalidOperationException("An invalid creator reached a turn.");
                    case Contract.Kinds.Start when turn.Header.TypeData == fullHeader || message.Payload == "small":
                        turn.State = "header-fit";
                        break;
                    case Contract.Kinds.Start when message.Payload is "header-update-over" or "header-child-over":
                        turn.Send(turn.Header.Ref, "speculative", dueAt: turn.Now + 1);
                        turn.State = "do-not-store";
                        if (message.Payload == "header-update-over") turn.SetHeader(fullHeader + "x", new Dictionary<string, string>());
                        else turn.Start(new(ModelType, fullHeader + "x", "header-fit"));
                        break;
                    case Contract.Kinds.Start:
                        turn.State = message.Payload switch
                        {
                            "overflow" => new string('x', atCap + 1),
                            "overstated" => "overstated",
                            _ => new string('x', atCap)
                        };
                        turn.Send(turn.Header.Ref, "speculative", dueAt: turn.Now + 1);
                        break;
                    case "large-fault":
                        throw new InvalidOperationException(new string('é', 200_000));
                    case "grow":
                        turn.State += "x";
                        turn.Send(turn.Header.Ref, "growth-leak", dueAt: turn.Now + 1);
                        break;
                    case Contract.Kinds.Terminate:
                        turn.State += new string('x', growth);
                        var labels = Enumerable.Range(0, 4).ToDictionary(index => new string('é', 511) + "x" + index,
                            _ => new string('é', 512));
                        turn.SetHeader(fullHeader, labels);
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                }
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            LegalJobEntered |= job.Key == new string('é', 512);
            return new((object?)null);
        }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => state == "overstated" ? long.MaxValue : growth;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "turn-cap";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var input = turn.Inbox.FirstOrDefault(message => message.Kind == Contract.Kinds.Start);
            if (input?.Payload is null) return default;
            if (input.Payload is "meta-fit" or "meta-over")
            {
                turn.Send(turn.Header.Ref, "before-metadata", dueAt: turn.Now + 1);
                turn.Send(turn.Header.Ref, new string('é', 128) + (input.Payload == "meta-over" ? "x" : ""), dueAt: turn.Now + 1);
                return default;
            }
            if (input.Payload.StartsWith("label", StringComparison.Ordinal))
            {
                var cap = new string('é', 512);
                var chars = new string('é', 511);
                var labels = Enumerable.Range(0, input.Payload == "label-count" ? 5 : 4)
                    .ToDictionary(index => chars + "x" + index.ToString(CultureInfo.InvariantCulture), _ => cap);
                if (input.Payload == "label-name")
                {
                    labels.Remove(chars + "x0");
                    labels.Add(cap + "x", cap);
                }
                if (input.Payload == "label-value") labels[chars + "x0"] = cap + "x";
                turn.State = Json.Write(input.Payload);
                turn.Send(turn.Header.Ref, "do-not-commit", dueAt: turn.Now + 1);
                turn.SetHeader("projected", labels);
                return default;
            }

            var sends = int.Parse(input.Payload, CultureInfo.InvariantCulture);
            for (var i = 0; i < sends; i++)
                turn.Send(turn.Header.Ref, "own-timer", dueAt: turn.Now + (long)TimeSpan.FromHours(1).TotalMilliseconds);
            turn.State = input.Payload;
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
