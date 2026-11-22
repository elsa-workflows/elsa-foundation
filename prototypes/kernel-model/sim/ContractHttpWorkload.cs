using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using Workflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>The legacy request modes through the public request API and its in-memory reply transport.</summary>
internal static class ContractHttpWorkload
{
    private sealed record Answer(Contract.ActorRef Actor, string Mode, string? Ambient, string? JobResult);

    internal static async Task<int> Run(int from, int seeds, int partitions, Flaw flaws)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            try { await new Schedule(seed, partitions, flaws).Run(); }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public request workload seed {seed} failed at {partitions} virtual partitions ({flaws}). {error.Message}");
                return 1;
            }
        }
        Console.WriteLine($"Public request workload passed {seeds} seeds from {from} at {partitions} virtual partitions. Each seed ran five legacy request modes.");
        return 0;
    }

    private sealed class Schedule
    {
        private const string ReceivingNode = "request-edge";
        private readonly Contract.BoundScope _scope = new("request-workload");
        private readonly Requests _requests = new(ReceivingNode);
        private readonly InMemoryStore _store;
        private readonly Contract.IIngress _ingress;
        private readonly Contract.IActorReads _reads;
        private readonly ReplySink _transport;
        private readonly Workflow _workflows;
        private readonly RequestModel _model;
        private readonly WorkerOptions _options;
        private readonly int _seed;
        private readonly int _delay;
        private long _now = 1_703_000_000_000;

        internal Schedule(int seed, int partitions, Flaw flaws)
        {
            _seed = seed;
            _delay = new Random(seed).Next(160, 301);
            _store = new(partitions, () => _now, flaws);
            _ingress = new InMemoryIngress(_store, _requests, ReceivingNode);
            _reads = new ActorList(_store, 8);
            _transport = new(_requests, _reads, _scope);
            _model = new RequestModel(_delay);
            _workflows = new([_model]);
            _options = new() { Lease = TimeSpan.FromSeconds(1), Partitions = partitions, Flaws = flaws,
                Monotonic = () => _now - 1_703_000_000_000, Transport = _transport };
        }

        internal async Task Run()
        {
            foreach (var mode in Http.Starts) await StartRequest(mode);
            await ResumeRequest();
        }

        private async Task StartRequest(string mode)
        {
            var early = mode == Http.Early && (_seed & 1) != 0;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var before = _transport.Delivered.Count;
            var pending = _ingress.RequestAsync(_scope, new Contract.Target.Start(Spec(mode)), mode, Contract.Kinds.Start,
                null, null, new("start ambient", _now + 5_000, ReleaseBeforeCommit: early), timeout.Token).AsTask();
            // The host locates the admitted creator; the schedule and result checks use the public surfaces.
            var partition = _store.Messages.Values.Single(item => item.Message.Id == mode).Message.Target.Partition;
            await using var owner = await Activate(ReceivingNode, partition);
            await Pass(owner);
            if (mode == Http.Timer)
            {
                _now += _delay - 1;
                await Pass(owner);
                Require(!pending.IsCompleted && _transport.Delivered.Count == before, "the request timer released output before its due reading");
                _now++;
                await Pass(owner);
            }
            await CheckReply(pending, before, mode, early, mode is Http.Respond or Http.Early ? "start ambient" : null);
        }

        private async Task ResumeRequest()
        {
            var admission = await _ingress.SendAsync(_scope, new Contract.Target.Start(Spec(Http.Wait)), "waiting-start", Contract.Kinds.Start);
            Require(admission is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the waiting workflow did not start");
            var actor = admission.Actor!.Value;
            var partition = new InstanceRef(_store.FoundationTarget(_scope.Tenant, actor.Id)!.Id).Partition;
            await using var first = await Activate("remote-owner", partition);
            await Pass(first);
            Require(await _reads.GetAsync(_scope, actor) is { Header.Status: Contract.ActorStatus.Running }, "the workflow did not reach its resume wait");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var before = _transport.Delivered.Count;
            var pending = _ingress.RequestAsync(_scope, new Contract.Target.Ref(actor), "resume-request", "resume", null, null,
                new("origin ambient", _now + 5_000), timeout.Token).AsTask();
            await first.PollAsync();
            await first.FlushAsync();
            // No further pass or release runs on the old owner. The successor finds the unconsumed request and reply route.
            _now += 1_001;
            await using var successor = await Activate("remote-successor", partition);
            await Pass(successor);
            _now += _delay - 1;
            await Pass(successor);
            Require(!pending.IsCompleted && _transport.Delivered.Count == before, "the successor released the resumed request before its timer");
            _now++;
            await Pass(successor);
            await CheckReply(pending, before, Http.Wait, early: false, ambient: null);
        }

        private async Task CheckReply(Task<Contract.Reply> pending, int before, string mode, bool early, string? ambient)
        {
            Contract.Reply? reply = null;
            try { reply = await pending; } catch (OperationCanceledException) { }
            var answer = reply?.Item as Answer;
            Require(reply is { Channel: "response", Error: null } && answer is not null &&
                answer.Mode == mode && answer.Ambient == ambient, $"{mode} returned the wrong public response or ambient");
            if (mode == Http.Work)
                Require(answer!.JobResult == "job:work" && _model.Jobs is [var job] && job == (answer.Actor, "work"),
                    "work did not answer from exactly one real job attempt");
            var deliveries = _transport.Delivered.Skip(before).Where(item => item.Value.Actor == answer!.Actor).ToArray();
            Require(deliveries.Length == 1 && deliveries[0].Committed != early,
                $"{mode} released its response on the wrong side of the commit");
            Require(await _reads.GetAsync(_scope, answer!.Actor) is { Header.Status: Contract.ActorStatus.Finished } &&
                (await _reads.InboxAsync(_scope, answer.Actor)).Items.Count == 0,
                $"{mode} did not finish once through its original public reference");
        }

        private async Task<Worker> Activate(string node, int partition)
        {
            var worker = new Worker(node, partition, _store, new ActorAdapter(_store, _workflows, (_requests, node)), _options);
            Require(await new DurableHost([worker]).ActivateAsync(partition) is not null, "the request owner could not activate");
            return worker;
        }

        private static async Task Pass(Worker owner)
        {
            for (var pass = 0; pass < 12; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }

        private static Contract.StartSpec Spec(string mode) => Workflow.Start(new("contract-http", "request", Json.Write(new HttpInput(mode))));
    }

    private sealed class ReplySink(Requests requests, Contract.IActorReads reads, Contract.BoundScope scope) : IReplyTransport
    {
        internal List<(Answer Value, bool Committed)> Delivered { get; } = [];

        public void Send(string node, Reply reply)
        {
            if (reply.Item is Answer answer)
            {
                var journal = reads.TailAsync(scope, answer.Actor).Result;
                Delivered.Add((answer, journal.Items.Any(entry => entry.Entry == Json.Write(answer))));
            }
            requests.Receive(reply);
        }
    }

    private sealed class RequestModel(int delay) : Models.IExecutionModel
    {
        public string Key => "contract-http";
        public Type StateType => typeof(HttpState);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => new HttpState { Mode = Json.Read<HttpInput>(start.Input!).Mode };
        public object UpgradeState(string state, int fromVersion) => throw new InvalidOperationException("No older request state exists.");
        internal List<(Contract.ActorRef Actor, string Key)> Jobs { get; } = [];

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Jobs.Add((job.Actor, job.Key));
            return new("job:" + job.Key);
        }
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;

        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var state = (HttpState)turn.State;
            foreach (var message in turn.Inbox)
            {
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when !state.Started:
                        state.Started = true;
                        if (state.Mode is Http.Respond or Http.Early) AnswerOnce();
                        if (state.Mode == Http.Respond) turn.SetStatus(Contract.ActorStatus.Finished);
                        if (state.Mode == Http.Early) turn.Send(turn.Instance.Ref, "continue");
                        if (state.Mode == Http.Timer) ScheduleTimer();
                        if (state.Mode == Http.Work) turn.StartJob("work");
                        break;
                    case "resume" when state.Mode == Http.Wait:
                        ScheduleTimer();
                        break;
                    case Contract.Kinds.JobFinished when state.Mode == Http.Work:
                        var result = Json.Read<Contract.JobFinished>(message.Payload!);
                        Require(result is { Key: "work", Result: not null, Error: null }, "work received the wrong job result");
                        AnswerOnce(Json.Read<string>(result.Result!));
                        turn.SetStatus(Contract.ActorStatus.Finished);
                        break;
                    case "continue" or "timer":
                        if (state.Mode != Http.Early) AnswerOnce();
                        turn.SetStatus(Contract.ActorStatus.Finished);
                        break;
                }
            }
            return default;

            void AnswerOnce(string? jobResult = null)
            {
                var answer = new Answer(turn.Instance.Ref, state.Mode, turn.Ambient as string, jobResult);
                turn.Record(answer);
                turn.Emit("response", answer);
            }

            void ScheduleTimer() => turn.Send(turn.Instance.Ref, "timer", dueAt: turn.Now + delay);
        }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }
}
