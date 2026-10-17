using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

const string packageId = "Microsoft.Agents.AI.Workflows";
const string packageVersion = "1.25.0";
var sourceDirectory = new DirectoryInfo(AppContext.BaseDirectory);
while (sourceDirectory is not null && !File.Exists(Path.Combine(sourceDirectory.FullName, "UnifiedExecution.AgentFrameworkProbe.csproj")))
    sourceDirectory = sourceDirectory.Parent;
if (sourceDirectory is null)
    throw new DirectoryNotFoundException("Could not locate the probe source directory from the application base path.");
using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sourceDirectory.FullName, "feed-manifest.json")));
var packageHash = manifest.RootElement.GetProperty("packages").EnumerateArray()
    .Single(p => p.GetProperty("id").GetString() == packageId && p.GetProperty("version").GetString() == packageVersion)
    .GetProperty("sha512Base64").GetString();
var results = new List<object>();

results.Add(await ProbeReceiverSchedulingAsync());
results.Add(await ProbeRoundBarrierAsync());
results.Add(await ProbeFanInCheckpointAsync());

var assembly = typeof(WorkflowBuilder).Assembly;
var evidence = new
{
    package = new
    {
        id = packageId,
        version = packageVersion,
        sha512Base64 = packageHash,
        assemblyVersion = assembly.GetName().Version?.ToString(),
        informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    },
    sourceReference = new
    {
        repository = "https://github.com/microsoft/agent-framework",
        commit = "44fe223da7ea6853eefb7053e25c2c213a2e5c15",
        packageCommitProvenance = "The 1.25.0 nuspec does not declare a source commit; the source SHA is a separate observation."
    },
    probes = results,
    limitations = new[]
    {
        "This is an in-process execution probe of the released NuGet package, not proof of distributed execution or Elsa hosting semantics.",
        "No LLM, connector, external network, retry, metrics, cancellation, or crash-after-side-effect behavior is exercised.",
        "The fan-in checkpoint probe uses the package in-memory checkpoint manager; it does not prove durable storage or cross-process restore."
    }
};

var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
var outputPath = Path.Combine(sourceDirectory.FullName, "evidence.json");
await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
Console.WriteLine(json);
Console.WriteLine($"Evidence written to {outputPath}");

static async Task<object> ProbeReceiverSchedulingAsync()
{
    var state = new ReceiverState();
    var start = Bind<SchedulingStart>("start", (id, _) => ValueTask.FromResult(new SchedulingStart(id)));
    var receiverA = Bind<SchedulingReceiver>("A", (id, _) => ValueTask.FromResult(new SchedulingReceiver(id, state, blockFirst: true)));
    var receiverB = Bind<SchedulingReceiver>("B", (id, _) => ValueTask.FromResult(new SchedulingReceiver(id, state, blockFirst: true)));
    var workflow = new WorkflowBuilder("start")
        .AddEdge(start, receiverA)
        .AddEdge(start, receiverB)
        .Build();

    await using var run = await InProcessExecution.Concurrent.OpenStreamingAsync(workflow);
    var watch = DrainAsync(run);
    var startAccepted = await run.TrySendMessageAsync(new StartScheduling());
    Assert(startAccepted, "workflow did not accept StartScheduling at its start executor");

    await WaitAsync(state.A1Entered.Task, "A1 entered");
    await WaitAsync(state.B1Entered.Task, "B1 entered while A1 remained held");
    state.ReleaseA1.TrySetResult();
    state.ReleaseB1.TrySetResult();
    await watch;

    Assert(state.AMaxActive == 1, $"same-receiver maximum concurrency was {state.AMaxActive}, expected 1");
    Assert(state.AMessages.SequenceEqual(["A1", "A2"]), "receiver A did not process both messages in order");
    Assert(state.BMessages.SequenceEqual(["B1"]), "receiver B did not process its message");
    Assert(state.BEnteredBeforeA1Released, "receiver B did not enter while A1 was held");

    return new
    {
        name = "concurrent receivers and serialized same receiver",
        passed = true,
        observed = new
        {
            receiverAOrder = state.AMessages.ToArray(),
            receiverAMaxActiveHandlers = state.AMaxActive,
            receiverBEnteredBeforeReceiverA1Released = state.BEnteredBeforeA1Released,
            receiverBMessageCount = state.BMessages.Count
        },
        assertions = new[]
        {
            "B entered while A1 was blocked, proving receiver-level overlap in this execution.",
            "A1 and A2 were queued to the same receiver; their handler active-count never exceeded one."
        }
    };
}

static async Task<object> ProbeRoundBarrierAsync()
{
    var state = new RoundState();
    var start = Bind<RoundStart>("start", (id, _) => ValueTask.FromResult(new RoundStart(id)));
    var fast = Bind<FastBranch>("fast", (id, _) => ValueTask.FromResult(new FastBranch(id, state)));
    var slow = Bind<SlowBranch>("slow", (id, _) => ValueTask.FromResult(new SlowBranch(id, state)));
    var fastNext = Bind<FastNext>("fast-next", (id, _) => ValueTask.FromResult(new FastNext(id, state)));
    var workflow = new WorkflowBuilder("start")
        .AddEdge(start, fast)
        .AddEdge(start, slow)
        .AddEdge(fast, fastNext)
        .Build();

    await using var run = await InProcessExecution.Concurrent.OpenStreamingAsync(workflow);
    var watch = DrainAsync(run);
    var startAccepted = await run.TrySendMessageAsync(new StartRound());
    Assert(startAccepted, "workflow did not accept StartRound at its start executor");
    await WaitAsync(state.FastSent.Task, "fast branch sent to next-round executor");
    await WaitAsync(state.SlowEntered.Task, "slow branch entered and remained blocked");
    Assert(!state.FastNextEntered.Task.IsCompleted, "fast-next entered before slow branch was released");
    state.ReleaseSlow.TrySetResult();
    await watch;

    Assert(state.FastNextEntered.Task.IsCompleted, "fast-next did not run after slow branch completed");
    Assert(state.FastNextObservedSlowComplete, "fast-next ran before the slow branch completion marker");

    return new
    {
        name = "round barrier holds fast branch next step for slow branch",
        passed = true,
        observed = new
        {
            fastMessageSentBeforeSlowRelease = true,
            fastNextEnteredAfterSlowCompletion = state.FastNextObservedSlowComplete,
            eventOrder = state.Events.ToArray()
        }
    };
}

static async Task<object> ProbeFanInCheckpointAsync()
{
    var manager = CheckpointManager.CreateInMemory();
    var beforeCheckpointState = new FanInState();
    var beforeCheckpointWorkflow = BuildFanInWorkflow(beforeCheckpointState);
    var execution = InProcessExecution.Concurrent.WithCheckpointing(manager);

    CheckpointInfo checkpoint;
    await using (var run = await execution.OpenStreamingAsync(beforeCheckpointWorkflow, "fan-in-probe"))
    {
        var watch = DrainAsync(run);
        var startAccepted = await run.TrySendMessageAsync(new FanInPhase("A"));
        Assert(startAccepted, "workflow did not accept fan-in phase A at its start executor");
        checkpoint = await watch ?? throw new InvalidOperationException("No checkpoint was emitted for the partial fan-in phase.");
    }

    Assert(beforeCheckpointState.JoinedItems.IsEmpty, "fan-in join ran before source B arrived");
    Assert(beforeCheckpointState.SourceAItemsProcessed == 2, "expected source A to process both messages before checkpoint");

    var afterResumeState = new FanInState();
    var afterResumeWorkflow = BuildFanInWorkflow(afterResumeState);
    await using (var resumed = await execution.ResumeStreamingAsync(afterResumeWorkflow, checkpoint))
    {
        var watch = DrainAsync(resumed);
        var startAccepted = await resumed.TrySendMessageAsync(new FanInPhase("B"));
        Assert(startAccepted, "resumed workflow did not accept fan-in phase B at its start executor");
        await watch;
    }

    Assert(afterResumeState.SourceAItemsProcessed == 0, "fresh source A executor observed replayed messages after restore");
    var joined = afterResumeState.JoinedItems.ToArray();
    Assert(joined.Length == 3, $"fan-in delivered {joined.Length} items, expected both A items and one B item");
    Assert(joined.Count(x => x.StartsWith("A:", StringComparison.Ordinal)) == 2, "fan-in did not preserve both duplicate-source arrivals");
    Assert(joined.Count(x => x.StartsWith("B:", StringComparison.Ordinal)) == 1, "fan-in did not deliver the B arrival");

    return new
    {
        name = "fan-in duplicate buffering survives partial checkpoint and resume",
        passed = true,
        observed = new
        {
            checkpointSessionId = checkpoint.SessionId,
            sourceAMessagesProcessedBeforeCheckpoint = beforeCheckpointState.SourceAItemsProcessed,
            sourceAMessagesReexecutedAfterResume = afterResumeState.SourceAItemsProcessed != 0,
            joinItemsBeforeResume = 0,
            joinItemsAfterResume = joined
        },
        assertions = new[]
        {
        "Source A processed two messages but they did not release the fan-in barrier before checkpoint.",
            "After checkpoint resume and one source B message, all three arrivals reached the join."
        }
    };
}

static async Task<CheckpointInfo?> DrainAsync(StreamingRun run)
{
    CheckpointInfo? lastCheckpoint = null;
    await foreach (var evt in run.WatchStreamAsync(blockOnPendingRequest: false))
    {
        if (evt is WorkflowErrorEvent { Exception: { } error })
            throw new InvalidOperationException("Workflow emitted an error event.", error);
        if (evt is SuperStepCompletedEvent { CompletionInfo.Checkpoint: { } checkpoint })
            lastCheckpoint = checkpoint;
    }
    return lastCheckpoint;
}

static async Task WaitAsync(Task task, string operation)
{
    try
    {
        await task.WaitAsync(TimeSpan.FromSeconds(20)); // deadlock guard only; no measured timing is used as evidence.
    }
    catch (TimeoutException e)
    {
        throw new TimeoutException($"Probe deadlocked while waiting for {operation}; timeout is a guard, not a semantic assertion.", e);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static ExecutorBinding Bind<TExecutor>(string id, Func<string, string, ValueTask<TExecutor>> factory)
    where TExecutor : Executor => factory.BindExecutor(id);

static Workflow BuildFanInWorkflow(FanInState state)
{
    var start = Bind<FanInStart>("start", (id, _) => ValueTask.FromResult(new FanInStart(id)));
    var a = Bind<FanInSource>("source-a", (id, _) => ValueTask.FromResult(new FanInSource(id, state)));
    var b = Bind<FanInSource>("source-b", (id, _) => ValueTask.FromResult(new FanInSource(id, state)));
    var join = Bind<FanInJoin>("join", (id, _) => ValueTask.FromResult(new FanInJoin(id, state)));
    return new WorkflowBuilder("start")
        .AddEdge(start, a)
        .AddEdge(start, b)
        .AddFanInBarrierEdge([a, b], join)
        .Build();
}

internal static class Signals
{
    public static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record StartScheduling;
internal sealed record ScheduledMessage(string Label);

internal sealed class ReceiverState
{
    public TaskCompletionSource A1Entered { get; } = Signals.New();
    public TaskCompletionSource B1Entered { get; } = Signals.New();
    public TaskCompletionSource ReleaseA1 { get; } = Signals.New();
    public TaskCompletionSource ReleaseB1 { get; } = Signals.New();
    public ConcurrentQueue<string> AMessages { get; } = new();
    public ConcurrentQueue<string> BMessages { get; } = new();
    public int AActive;
    public int AMaxActive;
    public volatile bool A1Released;
    public bool BEnteredBeforeA1Released;
}

internal sealed class SchedulingStart(string id) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder
        .SendsMessage<ScheduledMessage>()
        .ConfigureRoutes(routes =>
        routes.AddHandler<StartScheduling>(async (_, context) =>
        {
            await context.SendMessageAsync(new ScheduledMessage("A1"), "A");
            await context.SendMessageAsync(new ScheduledMessage("A2"), "A");
            await context.SendMessageAsync(new ScheduledMessage("B1"), "B");
        }));
}

internal sealed class SchedulingReceiver(string id, ReceiverState state, bool blockFirst) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder.ConfigureRoutes(routes =>
        routes.AddHandler<ScheduledMessage>(async (message, _) =>
        {
            if (Id == "A")
            {
                var active = Interlocked.Increment(ref state.AActive);
                UpdateMaximum(ref state.AMaxActive, active);
                state.AMessages.Enqueue(message.Label);
                if (message.Label == "A1")
                {
                    state.A1Entered.TrySetResult();
                    await state.ReleaseA1.Task;
                    state.A1Released = true;
                }
                Interlocked.Decrement(ref state.AActive);
            }
            else
            {
                state.BMessages.Enqueue(message.Label);
                state.BEnteredBeforeA1Released = !state.A1Released;
                state.B1Entered.TrySetResult();
                if (blockFirst)
                    await state.ReleaseB1.Task;
            }
        }));

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref maximum)) < value && Interlocked.CompareExchange(ref maximum, value, seen) != seen) { }
    }
}

internal sealed record StartRound;
internal sealed record RoundMessage(string Label);

internal sealed class RoundState
{
    public TaskCompletionSource FastSent { get; } = Signals.New();
    public TaskCompletionSource SlowEntered { get; } = Signals.New();
    public TaskCompletionSource ReleaseSlow { get; } = Signals.New();
    public TaskCompletionSource FastNextEntered { get; } = Signals.New();
    public ConcurrentQueue<string> Events { get; } = new();
    public volatile bool SlowComplete;
    public bool FastNextObservedSlowComplete;
}

internal sealed class RoundStart(string id) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder
        .SendsMessage<RoundMessage>()
        .ConfigureRoutes(routes =>
        routes.AddHandler<StartRound>(async (_, context) =>
        {
            await context.SendMessageAsync(new RoundMessage("to-fast"), "fast");
            await context.SendMessageAsync(new RoundMessage("to-slow"), "slow");
        }));
}

internal sealed class FastBranch(string id, RoundState state) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder
        .SendsMessage<RoundMessage>()
        .ConfigureRoutes(routes =>
        routes.AddHandler<RoundMessage>(async (_, context) =>
        {
            state.Events.Enqueue("fast.sent-fast-next");
            await context.SendMessageAsync(new RoundMessage("next"), "fast-next");
            state.FastSent.TrySetResult();
        }));
}

internal sealed class SlowBranch(string id, RoundState state) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder.ConfigureRoutes(routes =>
        routes.AddHandler<RoundMessage>(async (_, _) =>
        {
            state.Events.Enqueue("slow.entered");
            state.SlowEntered.TrySetResult();
            await state.ReleaseSlow.Task;
            state.SlowComplete = true;
            state.Events.Enqueue("slow.completed");
        }));
}

internal sealed class FastNext(string id, RoundState state) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder.ConfigureRoutes(routes =>
        routes.AddHandler<RoundMessage>((_, _) =>
        {
            state.FastNextObservedSlowComplete = state.SlowComplete;
            state.Events.Enqueue(state.FastNextObservedSlowComplete ? "fast-next.after-slow" : "fast-next.before-slow");
            state.FastNextEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }));
}

internal sealed record FanInPhase(string Source);
internal sealed record FanInData(string Source, string Item);

internal sealed class FanInState
{
    public int SourceAItemsProcessed;
    public ConcurrentQueue<string> JoinedItems { get; } = new();
}

internal sealed class FanInStart(string id) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder
        .SendsMessage<FanInData>()
        .ConfigureRoutes(routes =>
        routes.AddHandler<FanInPhase>(async (phase, context) =>
        {
            if (phase.Source == "A")
            {
                await context.SendMessageAsync(new FanInData("A", "A-1"), "source-a");
                await context.SendMessageAsync(new FanInData("A", "A-2"), "source-a");
            }
            else if (phase.Source == "B")
            {
                await context.SendMessageAsync(new FanInData("B", "B-1"), "source-b");
            }
            else
            {
                throw new InvalidOperationException($"Unknown fan-in phase {phase.Source}.");
            }
        }));
}

internal sealed class FanInSource(string id, FanInState state) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder
        .SendsMessage<FanInData>()
        .ConfigureRoutes(routes =>
        routes.AddHandler<FanInData>(async (message, context) =>
        {
            if (message.Source == "A")
                Interlocked.Increment(ref state.SourceAItemsProcessed);
            await context.SendMessageAsync(message, "join");
        }));
}

internal sealed class FanInJoin(string id, FanInState state) : Executor(id)
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder.ConfigureRoutes(routes =>
        routes.AddHandler<FanInData>((message, _) =>
        {
            state.JoinedItems.Enqueue($"{message.Source}:{message.Item}");
            return ValueTask.CompletedTask;
        }));
}
