using System.Globalization;
using System.Text.Json;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ContractWorkload
{
    private const string Model = "contract-workload";
    private const string Signal = "signal";
    private const string Credit = "credit";
    private const string Timer = "scheduled-credit";
    private const string ReceiptCount = "receipts";

    public static Task<int> Run(int from, int seeds, int actions, int partitions, Flaw flaws) =>
        Run(from, seeds, actions, $"at {partitions} virtual partitions ({flaws})", actor => new InMemoryDriver(partitions, flaws, actor));

    internal static async Task<int> Run(int from, int seeds, int actions, string profile, Func<Contract.IActorType, IDriver> createDriver)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        ArgumentOutOfRangeException.ThrowIfNegative(actions);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            var trace = new List<string>();
            try
            {
                await using var driver = createDriver(new ReceiptActor());
                await new Schedule(driver, trace).Run(seed, actions);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public contract seed {seed} failed {profile}. {error.Message}");
                Console.Error.WriteLine(string.Join(Environment.NewLine, trace));
                return 1;
            }
        }

        Console.WriteLine($"Public contract workload passed {seeds} seeds from {from} {profile}, {actions} generated actions per seed. Each seed observed a takeover and both commit-loss boundaries.");
        return 0;
    }

    private sealed record Receipt(string Token, string Kind, long At);
    private sealed record Expected(string Kind, long Due);

    private sealed class Subject(Contract.BoundScope scope, string callerKey, Contract.ActorRef reference)
    {
        public Contract.BoundScope Scope { get; } = scope;
        public string CallerKey { get; } = callerKey;
        public Contract.ActorRef Reference { get; } = reference;
        public readonly Dictionary<string, Expected> Expected = new(StringComparer.Ordinal);
        public readonly List<string> Credits = [];
    }

    // The schedule and its oracle run over these public surfaces. Each foundation plugs in its own clock and fault controls.
    internal interface IDriver : IAsyncDisposable
    {
        Contract.IIngress Ingress { get; }
        Contract.IActorReads Reads { get; }
        long Now { get; }
        void Advance(long milliseconds);
        Task Start(IReadOnlyList<(Contract.BoundScope Scope, Contract.ActorRef Ref)> actors);
        Task Pass(int count);
        Task TakeOver();
        Task LoseTurnCommit(bool landed);
    }

    private sealed class Schedule(IDriver driver, List<string> trace)
    {
        private readonly List<Subject> _subjects = [];
        private int _operation;

        internal async Task Run(int seed, int actions)
        {
            var random = new Random(seed);
            foreach (var tenant in new[] { "north", "south" })
            foreach (var key in new[] { "Work", "Counters" })
            {
                var scope = new Contract.BoundScope(tenant);
                var spec = new Contract.StartSpec(Model, key);
                trace.Add($"open {tenant}/{key} and retry its start with alternate casing");
                var opened = await driver.Ingress.SendAsync(scope, new Contract.Target.Key(key, spec), "start", Contract.Kinds.Start);
                Require(opened.Outcome == Contract.AdmissionOutcome.Accepted && opened.Actor.HasValue,
                    $"start {tenant}/{key} did not return an opaque ref");
                var duplicate = await driver.Ingress.SendAsync(scope, new Contract.Target.Key(key.ToUpperInvariant(), spec), "start", Contract.Kinds.Start);
                Require(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && duplicate.Actor == opened.Actor,
                    $"retry of {tenant}/{key} did not return the pinned ref");
                _subjects.Add(new(scope, key, opened.Actor!.Value));
            }

            Require(_subjects.Select(subject => subject.Reference).Distinct().Count() == _subjects.Count,
                "the same caller keys in separate tenants shared an actor ref");
            await driver.Start(_subjects.Select(subject => (subject.Scope, subject.Reference)).ToArray());
            await Pass();
            await CheckIsolation();
            foreach (var subject in _subjects)
                Require((await driver.Reads.InboxAsync(subject.Scope, subject.Reference)).Items.Count == 0,
                    $"the committed creator remained in {subject.Scope.Tenant}/{subject.CallerKey}'s public inbox");

            await Send(_subjects[0], keyed: false, due: driver.Now + 10);
            await Pass();
            var pending = await driver.Reads.InboxAsync(_subjects[0].Scope, _subjects[0].Reference);
            Require(pending.Items.Any(input => input is { Kind: Timer, State: Contract.DeliveryState.Pending } && input.Due == driver.Now + 10),
                "future input must still be pending before its due time");
            await Move();
            await Lose(landed: false);
            await Lose(landed: true);

            for (var i = 0; i < actions; i++)
            {
                var subject = _subjects[random.Next(_subjects.Count)];
                switch (random.Next(9))
                {
                    case 0:
                    case 1:
                        await Send(subject, keyed: false);
                        break;
                    case 2:
                        await Send(subject, keyed: true);
                        break;
                    case 3:
                        await Send(subject, keyed: false, due: driver.Now + random.Next(1, 26));
                        break;
                    case 4:
                        await Publish(subject.Scope);
                        break;
                    case 5:
                        driver.Advance(random.Next(1, 9));
                        trace.Add($"advance to {driver.Now}");
                        await Pass(1);
                        break;
                    case 6:
                        await Move();
                        break;
                    case 7:
                        await Lose(landed: random.Next(2) == 0);
                        break;
                    case 8:
                        await Pass();
                        break;
                }
            }

            await Move();
            await Pass(_operation + 4);
            foreach (var subject in _subjects) await CheckJournal(subject);
        }

        private async Task CheckIsolation()
        {
            var subject = _subjects[0];
            var stranger = _subjects.First(actor => actor.Scope.Tenant != subject.Scope.Tenant);
            var admitted = await driver.Ingress.SendAsync(stranger.Scope, new Contract.Target.Ref(subject.Reference), "foreign", Credit, "foreign");
            Require(admitted.Outcome == Contract.AdmissionOutcome.Refused &&
                await driver.Reads.GetAsync(stranger.Scope, subject.Reference) is null &&
                (await driver.Reads.TailAsync(stranger.Scope, subject.Reference)).Items.Count == 0 &&
                (await driver.Reads.InboxAsync(stranger.Scope, subject.Reference)).Items.Count == 0,
                "an exact ref crossed the tenant boundary");
        }

        private async Task Send(Subject subject, bool keyed, long? due = null)
        {
            var token = Next();
            Contract.Target target = keyed ? new Contract.Target.Key(subject.CallerKey.ToLowerInvariant()) : new Contract.Target.Ref(subject.Reference);
            var kind = due.HasValue ? Timer : Credit;
            Contract.AddOptions? options = due.HasValue ? new(DueAt: due) : null;
            trace.Add($"send {token} {kind} to {subject.Scope.Tenant}/{subject.CallerKey} ({(keyed ? "key" : "ref")}) due {due}");
            var first = await driver.Ingress.SendAsync(subject.Scope, target, token, kind, token, options);
            var repeat = await driver.Ingress.SendAsync(subject.Scope, target, token, kind, token, options);
            Require(first is { Outcome: Contract.AdmissionOutcome.Accepted } && first.Actor == subject.Reference &&
                repeat is { Outcome: Contract.AdmissionOutcome.Duplicate } && repeat.Actor == subject.Reference,
                $"send {token} did not deduplicate to the pinned ref");
            subject.Expected.Add(token, new(kind, due ?? driver.Now));
            if (!due.HasValue) subject.Credits.Add(token);
        }

        private async Task Publish(Contract.BoundScope scope)
        {
            var token = Next();
            var target = new Contract.Target.Event(Signal);
            var options = new Contract.AddOptions(KeepFor: TimeSpan.FromHours(1));
            trace.Add($"publish {token} to {scope.Tenant}/{Signal}");
            var first = await driver.Ingress.SendAsync(scope, target, token, Contract.Kinds.Event, token, options);
            var repeat = await driver.Ingress.SendAsync(scope, target, token, Contract.Kinds.Event, token, options);
            Require(first.Outcome == Contract.AdmissionOutcome.Accepted && repeat.Outcome == Contract.AdmissionOutcome.Duplicate,
                $"publish {token} did not deduplicate");
            foreach (var subject in _subjects.Where(actor => actor.Scope.Tenant == scope.Tenant))
                subject.Expected.Add(token, new(Contract.Kinds.Event, driver.Now));
        }

        private async Task Lose(bool landed)
        {
            await Send(_subjects[0], keyed: false);
            trace.Add(landed ? "lose the answer to a landed turn commit" : "lose a turn commit before it executes");
            await driver.LoseTurnCommit(landed);
        }

        private async Task Move()
        {
            trace.Add($"expire the owner at {driver.Now} and take again");
            await driver.TakeOver();
        }

        private async Task Pass(int passes = 6)
        {
            trace.Add($"run {passes} passes at {driver.Now}");
            await driver.Pass(passes);
        }

        private async Task CheckJournal(Subject subject)
        {
            var label = $"{subject.Scope.Tenant}/{subject.CallerKey}";
            var remaining = new Dictionary<string, Expected>(subject.Expected, StringComparer.Ordinal);
            var credits = new List<string>();
            Contract.PageToken? tail = null;
            for (var pageNumber = 0; pageNumber <= subject.Expected.Count + 1; pageNumber++)
            {
                var page = await driver.Reads.TailAsync(subject.Scope, subject.Reference, tail);
                if (page.Items.Count == 0) break;
                Require(page.Next is not null && page.Next != tail, $"{label} returned a stationary journal cursor");
                tail = page.Next;
                foreach (var entry in page.Items)
                {
                    using var document = JsonDocument.Parse(entry.Entry);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty(nameof(Receipt.Token), out _)) continue;
                    var received = document.RootElement.Deserialize<Receipt>()!;
                    Require(remaining.Remove(received.Token, out var expected),
                        $"{label} applied duplicate or foreign receipt {received.Token}");
                    Require(received.Kind == expected!.Kind && received.At >= expected.Due,
                        $"{label} applied {received.Token} with the wrong kind or before admission or due");
                    if (received.Kind == Credit) credits.Add(received.Token);
                }
            }

            Require(remaining.Count == 0, $"{label} is missing {string.Join(", ", remaining.Keys)}");
            Require(credits.SequenceEqual(subject.Credits), $"{label} did not preserve ordinary input order");
            var view = await driver.Reads.GetAsync(subject.Scope, subject.Reference);
            Require(view is { Header.Status: Contract.ActorStatus.Running, Park: null } &&
                view.Header.Labels.GetValueOrDefault(ReceiptCount) == subject.Expected.Count.ToString(CultureInfo.InvariantCulture) &&
                (await driver.Reads.InboxAsync(subject.Scope, subject.Reference)).Items.Count == 0,
                $"{label} checkpoint or inbox disagrees with the journal");
        }

        private string Next() => (++_operation).ToString(CultureInfo.InvariantCulture);
    }

    private sealed class ReceiptActor : Contract.IActorType
    {
        public string Type => Model;
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var processed = turn.State is { } state ? int.Parse(state, CultureInfo.InvariantCulture) : 0;
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.Subscribe(Signal, Signal);
                    continue;
                }
                if (message.Kind == Contract.Kinds.Event && message.Event is not { Key: Signal, Wait: Signal })
                    throw new InvalidOperationException("event envelope lost its key or wait");
                turn.Record(new Receipt(message.Payload!, message.Kind, turn.Now));
                processed++;
            }
            turn.State = processed.ToString(CultureInfo.InvariantCulture);
            turn.SetHeader(turn.Header.TypeData, new Dictionary<string, string> { [ReceiptCount] = turn.State });
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [], [new(Signal, Signal, Keep: false)]);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class InMemoryDriver : IDriver
    {
        private const long Initial = 1_702_000_000_000;
        private const long Lease = 50;
        private readonly InMemoryStore _store;
        private readonly IKernelStore _wire;
        private readonly WorkerOptions _options;
        private readonly List<IScopeActivation> _owners = [];
        private readonly Contract.IActorType _type;
        private int[] _placements = [];
        private int _node;
        private bool? _loseAt;

        internal InMemoryDriver(int partitions, Flaw flaws, Contract.IActorType type)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(partitions, 2);
            _type = type;
            _store = new(partitions, () => Now, flaws);
            _wire = new CountingStore(_store, () => { }, ObserveCommit);
            Ingress = new InMemoryIngress(_store, new Requests(Model), Model);
            Reads = new ActorList(_store, 7);
            _options = new()
            {
                Partitions = partitions, Lease = TimeSpan.FromMilliseconds(Lease), MaxAttempts = 10,
                Monotonic = () => Now - Initial, WallClock = () => Now + 30, Flaws = flaws
            };
        }

        public Contract.IIngress Ingress { get; }
        public Contract.IActorReads Reads { get; }
        public long Now { get; private set; } = Initial;

        public void Advance(long milliseconds) => Now += milliseconds;

        public async Task Start(IReadOnlyList<(Contract.BoundScope Scope, Contract.ActorRef Ref)> actors)
        {
            // Placement is private to the fault driver. The schedule only has the opaque refs given by ingress.
            _placements = actors.Select(actor =>
                new InstanceRef(_store.FoundationTarget(actor.Scope.Tenant, actor.Ref.Id)!.Id).Partition)
                .Distinct().Order().ToArray();
            Require(_placements.Length > 1, "the fault driver must exercise multiple virtual partitions");
            await Take();
        }

        public async Task TakeOver()
        {
            var old = _owners.ToArray();
            _owners.Clear();
            Advance(Lease + 1);
            await Take();
            foreach (var owner in old)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await owner.DisposeAsync();
            }
        }

        public async Task LoseTurnCommit(bool landed)
        {
            _loseAt = landed;
            await Pass(6);
            Require(_loseAt is null, "the scheduled commit loss was not reached");
        }

        public async Task Pass(int count)
        {
            for (var pass = 0; pass < count; pass++)
            {
                await _owners[0].WokenAsync(null);
                await _owners[0].RunAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var owner in _owners) await owner.DisposeAsync();
        }

        private async Task Take()
        {
            var host = DurableHost.ForNode($"contract-node-{++_node}", _wire, new ActorAdapter(_store, _type), _options.Partitions, _options);
            foreach (var partition in _placements)
            {
                var activation = await host.ActivateAsync(partition);
                Require(activation is not null, "successor could not acquire the expired scope");
                _owners.Add(activation!);
            }
        }

        private void ObserveCommit(CommitBatch batch, bool landed)
        {
            if (_loseAt != landed || batch.Positions == 0) return;
            _loseAt = null;
            throw new StoreException(Failure.Unknown, "simulated loss of the turn commit answer");
        }
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
