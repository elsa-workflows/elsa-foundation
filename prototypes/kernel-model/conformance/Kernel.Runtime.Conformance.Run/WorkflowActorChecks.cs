using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;
using Kernel.WorkflowActor;

internal static class WorkflowActorChecks
{
    private const long Now = 1_700_000_000_000;

    public static async Task<int> RunAsync()
    {
        var faults = 0;
        void Check(bool ok, string title)
        {
            Console.WriteLine($"{(ok ? "ok" : "FAIL")} {title}");
            if (!ok) faults++;
        }

        var actor = new WorkflowActor([new Probe()]);
        var start = WorkflowActor.Start(new WorkflowStart("probe", "initial", "\"input\""));
        var header = new ActorHeader(new("a297"), start.Type, "acme", ActorStatus.Running, null, null, null, start.TypeData,
            new Dictionary<string, string>());
        var turn = new RecordingTurn(header, null, [new("birth", Kinds.Start, start.Input)]);
        await actor.TurnAsync(turn, CancellationToken.None);
        Check(turn.State == "\"input\"" && turn.Header.Labels.GetValueOrDefault("definition") == "rebound",
            "the workflow actor creates typed state and persists its rebound definition through the contract");
        Check(turn.Sent is { Target.Id: "a297", Payload: "[1,2]", DueAt: Now + 1 },
            "the workflow actor serializes a model send and carries the opaque target and due time unchanged");
        Check(turn.Job is { Key: "job", Timeout: var timeout } && timeout == TimeSpan.FromSeconds(2),
            "the model's background job timeout crosses the actor boundary");

        var plan = actor.RestoreWaits(turn.Header, turn.State!);
        Check(turn.Job is { } job && plan.Jobs is [{ Key: "job" } restored] && restored.Arguments == job.Arguments &&
            plan.Timers is [{ Due: Now + 1 }] && plan.Subscriptions is [{ Keep: true }],
            "restoration uses the same invocation recipe as the first job start");
        var result = await actor.ExecuteJobAsync(new Job(header.Ref, turn.Job?.Arguments), CancellationToken.None);
        Check(Equals(result, "a297:job:e803:1:100:job-args"), "background execution preserves the job attempt context and model arguments");

        var suspend = new RecordingTurn(turn.Header, turn.State, [new("s", Kinds.Suspend)]);
        await actor.TurnAsync(suspend, CancellationToken.None);
        var resume = new RecordingTurn(suspend.Header, suspend.State, [new("r", Kinds.Resume)]);
        await actor.TurnAsync(resume, CancellationToken.None);
        Check(actor.Withholds(suspend.Header) && !actor.Withholds(resume.Header) && suspend.State == turn.State && resume.State == turn.State,
            "suspend and resume change withholding through the header and leave workflow state intact");
        var finish = new RecordingTurn(resume.Header, resume.State, [new("end", "finish")]);
        await actor.TurnAsync(finish, CancellationToken.None);
        Check(finish.Ended is { Status: ActorStatus.Finished, Output: "input" }, "the model's terminal result crosses the actor boundary");
        return faults == 0 ? 0 : 1;
    }

    private sealed class Probe : IExecutionModel
    {
        public string Key => "probe";
        public Type StateType => typeof(string);
        public int StateVersion => 1;
        public object CreateState(WorkflowStart start) => JsonSerializer.Deserialize<string>(start.Input!)!;
        public object UpgradeState(string state, int fromVersion) => throw new InvalidOperationException("Unexpected version.");

        public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox[0].Kind == Kinds.Start)
            {
                turn.Rebind("rebound");
                turn.Send(turn.Instance.Ref, "timer", new[] { 1, 2 }, turn.Now + 1);
                turn.StartJob("job", "job-args", TimeSpan.FromSeconds(2));
            }
            else if (turn.Inbox[0].Kind == "finish")
                turn.SetStatus(ActorStatus.Finished, output: turn.State);
            else throw new InvalidOperationException("Unexpected input.");
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) =>
            new($"{job.Actor.Id}:{job.Key}:{job.AttemptId}:{job.AttemptNumber}:{job.Deadline}:{JsonSerializer.Deserialize<string>(job.Arguments!)}");

        public RestorePlan RestoreWaits(InstanceInfo instance, object state) =>
            new([new("timer", "timer", "[1,2]", Now + 1)], [new("job", "\"job-args\"")], [new("event", "wait", true)]);

        public long TerminalGrowth(InstanceInfo instance, object state) => 0;
    }

    private sealed record Job(ActorRef Actor, string? Arguments) : IJob
    {
        public string Key => "job";
        public string AttemptId => "e803";
        public int AttemptNumber => 1;
        public long? Deadline => 100;
    }

    private sealed class RecordingTurn(ActorHeader header, string? state, IReadOnlyList<Message> inbox) : IActorTurn
    {
        public ActorHeader Header { get; private set; } = header;
        public long Now => WorkflowActorChecks.Now;
        public IReadOnlyList<Message> Inbox { get; } = inbox;
        public string? State { get; set; } = state;
        public object? Ambient => null;
        public (ActorRef Target, string? Payload, long? DueAt)? Sent { get; private set; }
        public (string Key, string? Arguments, TimeSpan? Timeout)? Job { get; private set; }
        public (ActorStatus Status, object? Output)? Ended { get; private set; }

        public string Send(ActorRef target, string kind, string? payload, long? dueAt)
        {
            Sent = (target, payload, dueAt);
            return "timer";
        }

        public void StartJob(string key, string? arguments, TimeSpan? timeout) => Job = (key, arguments, timeout);
        public void End(ActorStatus status, object? output) => Ended = (status, output);
        public void SetHeader(string typeData, IReadOnlyDictionary<string, string> labels) => Header = Header with { TypeData = typeData, Labels = labels };

        public void Emit(string channel, object? item) => throw Unexpected();
        public void Reply(Message message, string channel, object? item) => throw Unexpected();
        public void Recall(string id) => throw Unexpected();
        public ActorRef Start(StartSpec spec, Placement placement, bool detached) => throw Unexpected();
        public void CancelJob(string key) => throw Unexpected();
        public void Hold(Message message) => throw Unexpected();
        public void Subscribe(string key, string wait, bool keep) => throw Unexpected();
        public void Unsubscribe(string key, string wait) => throw Unexpected();
        public string Publish(string key, string? payload, TimeSpan keepFor) => throw Unexpected();
        public void Record(object entry) => throw Unexpected();

        private static InvalidOperationException Unexpected() => new("This call is outside the workflow actor boundary witness.");
    }
}
