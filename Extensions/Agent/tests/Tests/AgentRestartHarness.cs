using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T009: simulates host restarts. Each <see cref="RecoveryHost"/> is a complete default composition; a restart
/// disposes it and builds a new one over the same round-tripping record store, the same clock and the same owning
/// module, whose execution count is the authority on how often an effect ran. Only the store is shared: every other
/// in-memory object, including the proposal store and claims, belongs to one host and is lost on restart.
/// </summary>
internal sealed class AgentRestartHarness : IDisposable
{
    /// <summary>
    /// The store every harness builds when a test names none: the round-tripping in-memory store, unless a test assembly
    /// sets another once at load (the durable EF suite runs these same scenarios over SQLite this way).
    /// </summary>
    internal static Func<TimeProvider, IAgentSessionRecordStore>? DefaultStore { get; set; }

    /// <summary>Releases a store <see cref="DefaultStore"/> built (its database, for example) when its harness ends.</summary>
    internal static Action<IAgentSessionRecordStore>? ReleaseDefaultStore { get; set; }

    /// <param name="store">Builds the shared store over the harness clock; the default is <see cref="DefaultStore"/>.</param>
    /// <param name="delegated">Runs turns through a delegated (harness-owned) loop, as the Copilot adapter does, instead of the host-driven loop.</param>
    public AgentRestartHarness(Func<TimeProvider, IAgentSessionRecordStore>? store = null, Action<IServiceCollection>? configure = null, bool delegated = false)
    {
        Provider = delegated ? new HeldDelegatedProvider() : new HeldTurnProvider();
        if (store is null && DefaultStore is { } defaultStore)
        {
            Store = defaultStore(Clock);
            _ownsDefaultStore = true;
        }
        else Store = store?.Invoke(Clock) ?? new InMemoryAgentSessionRecordStore(Clock);
        _configure = configure;
        Host = new RecoveryHost(this);
    }

    private readonly Action<IServiceCollection>? _configure;
    private readonly bool _ownsDefaultStore;

    public RestartTestClock Clock { get; } = new();
    public IAgentSessionRecordStore Store { get; }
    public RecoveryModuleTool Module { get; } = new();
    public IRecoveryTestProvider Provider { get; }
    public RecoveryHost Host { get; private set; }

    /// <summary>
    /// Disposes the current host without any graceful shutdown, as a crash would, and builds a new one. By default
    /// the clock first moves past the old host's lease, which a crashed process can no longer renew.
    /// </summary>
    public RecoveryHost Restart(bool afterLease = true)
    {
        Host.Dispose();
        if (afterLease) Clock.Advance(InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));
        return Host = new RecoveryHost(this);
    }

    /// <summary>A second, concurrently running host over the same store, clock and module (another process).</summary>
    public RecoveryHost StartAnotherHost() => new(this);

    internal void Configure(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        services.Replace(ServiceDescriptor.Singleton(Store));
        services.AddSingleton<IAgentProvider>(Provider);
        services.AddSingleton<IAgentTool>(Module);
        _configure?.Invoke(services);
    }

    public void Dispose()
    {
        Host.Dispose();
        if (_ownsDefaultStore) ReleaseDefaultStore?.Invoke(Store);
    }
}

internal sealed class RecoveryHost : IDisposable
{
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;

    public RecoveryHost(AgentRestartHarness harness)
    {
        _root = AgentTestServices.Create(harness.Configure);
        _scope = _root.CreateScope();
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public IAgentSessionService Sessions => Services.GetRequiredService<IAgentSessionService>();
    public InMemoryAgentSessionService Owner => (InMemoryAgentSessionService)Sessions;
    public IAgentStreamingService Streaming => Services.GetRequiredService<IAgentStreamingService>();
    public IAgentProposalService Proposals => Services.GetRequiredService<IAgentProposalService>();
    public IAgentToolInvoker Invoker => Services.GetRequiredService<IAgentToolInvoker>();
    public IAgentSessionPolicyService Policy => Services.GetRequiredService<IAgentSessionPolicyService>();
    public IAgentAuditReader Audit => Services.GetRequiredService<IAgentAuditReader>();
    public IAgentTurnStateStore Pauses => Services.GetRequiredService<IAgentTurnStateStore>();
    public InMemoryAgentProposalStore ProposalStore => Services.GetRequiredService<InMemoryAgentProposalStore>();

    public Task<AgentSession> CreateAsync(AgentPolicy? policy = null) => Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1",
        DeterministicAgentProvider.Id, "explain", "Recovery session", policy ?? AgentPolicy.Default, new Dictionary<string, string>()));

    public Task<AgentMessage> PostAsync(AgentSession session, string content, string? requestId = null, string? fingerprint = null)
        => Sessions.AddMessageAsync(session.Id, new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], [])
        { RequestId = requestId, DeliveryFingerprint = fingerprint });

    public async Task<List<AgentStreamEvent>> StreamAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in Streaming.StreamAsync(session.Id, cancellationToken))
            events.Add(item);
        return events;
    }

    /// <summary>Prompts a mutation and runs the turn until it pauses on the proposal for review.</summary>
    public async Task<AgentActionProposal> PauseOnReviewAsync(AgentSession session)
    {
        await PostAsync(session, "mutate the resource");
        var events = await StreamAsync(session);
        return Assert.IsType<AgentActionProposal>(Assert.Single(events, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
    }

    public Task<AgentSessionPolicyResult> ProbeAsync(AgentSession session, long revision = 1)
        => Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.AutoReadOnly, revision));

    public async Task<IReadOnlyList<AgentAuditEvent>> AuditAsync(AgentSession session) => [.. await Audit.ListAsync(session.Id)];

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
    }
}

/// <summary>The owning module: a reviewed server mutation whose dispatches are counted across every host.</summary>
internal sealed class RecoveryModuleTool : IAgentTool
{
    private int _dispatches;
    private int _effects;
    private (TaskCompletionSource Started, TaskCompletionSource Release)? _hold;

    /// <summary>How many times the owning module was dispatched, across every host.</summary>
    public int Dispatches => Volatile.Read(ref _dispatches);
    /// <summary>How many effects the owning module actually applied, across every host.</summary>
    public int Effects => Volatile.Read(ref _effects);
    /// <summary>How many times the owning module prepared an operation, across every host.</summary>
    public int Preparations => Volatile.Read(ref _preparations);
    private int _preparations;
    public string Revision { get; private set; } = "revision-1";
    /// <summary>The installed tool's current risk; raising it requires a new review.</summary>
    public AgentRisk Risk { get; set; } = AgentRisk.ReviewRequired;
    /// <summary>The permissions the current principal needs to apply the change; none by default.</summary>
    public IReadOnlyCollection<string> RequiredPermissions { get; set; } = [];

    public AgentToolDescriptor Descriptor => new(DeterministicAgentProvider.MutatingToolName, "Apply change", "Applies a test resource change.",
        AgentToolMutability.Mutating, Risk, """{"type":"object","properties":{"change":{"type":"string"}}}""", RequiredPermissions, [])
    { ModuleId = "test.module", DisclosureSourceId = AgentTestContextSource.TestModuleSourceId, ResultType = "test.change", ResultSchemaVersion = "1" };

    /// <summary>A user edit in the owning module moves the resource to a new revision.</summary>
    public void ChangeByUser() => Revision += "-edited";

    /// <summary>Holds the next dispatch after it is counted and before it reports anything.</summary>
    public (TaskCompletionSource Started, TaskCompletionSource Release) HoldNextDispatch()
        => (_hold = (new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously))).Value;

    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _preparations);
        return Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with
        { Target = new("test.module", "test.resource", "resource-1", Revision) }));
    }

    public async Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _dispatches);
        if (_hold is { } hold)
        {
            _hold = null;
            hold.Started.TrySetResult();
            await hold.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        if (invocation.Target?.Revision != Revision)
            return AgentResult<AgentToolResult>.Failure("test.revision_changed", "The resource moved since preparation.", 409);
        Revision = $"revision-{count + 1}";
        Interlocked.Increment(ref _effects);
        var data = new AgentPayload("test.change", "1", JsonSerializer.SerializeToElement(new { dispatches = count }));
        return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "applied", data)
        { Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = Revision }, AgentEffectScope.ServerPersisted, data) });
    }
}

/// <summary>What a provider was given for one turn, on either loop.</summary>
internal sealed record RecordedTurn(IReadOnlyList<AgentTurnMessage> History, IReadOnlyList<AgentToolResult> PendingToolResults);

/// <summary>A recovery test provider: its next turn can be held mid-provider, and every turn's input is recorded.</summary>
internal interface IRecoveryTestProvider : IAgentProvider
{
    int Turns { get; }
    List<RecordedTurn> Contexts { get; }
    (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext();
}

/// <summary>The deterministic host-driven provider; its next turn can be held mid-provider, and every context is recorded.</summary>
internal sealed class HeldTurnProvider : IRecoveryTestProvider
{
    private readonly DeterministicAgentProvider _inner = new();
    private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;
    private int _turns;

    public string ProviderId => _inner.ProviderId;
    public int Turns => Volatile.Read(ref _turns);
    public List<RecordedTurn> Contexts { get; } = [];

    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
    {
        var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_inner) _next = hold;
        return hold;
    }

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);

    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _turns);
        (TaskCompletionSource Entered, TaskCompletionSource Release)? hold;
        lock (_inner)
        {
            Contexts.Add(new(context.History, context.PendingToolResults));
            hold = _next;
            _next = null;
        }
        if (hold is { } held)
        {
            yield return new(Core.Services.AgentProviderPrimitives.NewId(), AgentStreamEventKind.MessageDelta, "Partial ", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
            held.Entered.TrySetResult();
            await held.Release.Task.WaitAsync(cancellationToken);
        }
        await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken))
            yield return item;
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
}

/// <summary>
/// The deterministic delegated provider: it owns the turn's loop as the Copilot adapter does, dispatching the same
/// reviewed mutation through the server's tool invoker and reporting a created proposal as a stream event, and it receives
/// a recorded receipt as a continuation. Its next turn can be held mid-provider, and every request is recorded.
/// </summary>
internal sealed class HeldDelegatedProvider : IRecoveryTestProvider, IAgentHarness
{
    private readonly object _gate = new();
    private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;
    private int _turns;

    public string ProviderId => DeterministicAgentProvider.Id;
    public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools;
    public int Turns => Volatile.Read(ref _turns);
    public List<RecordedTurn> Contexts { get; } = [];

    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
    {
        var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_gate) _next = hold;
        return hold;
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _turns);
        (TaskCompletionSource Entered, TaskCompletionSource Release)? hold;
        lock (_gate)
        {
            Contexts.Add(new(request.History, request.PendingToolResults));
            hold = _next;
            _next = null;
        }
        await Task.Yield();
        if (hold is { } held)
        {
            yield return Delta("Partial ");
            held.Entered.TrySetResult();
            await held.Release.Task.WaitAsync(cancellationToken);
        }
        if (request.PendingToolResults.Count > 0)
        {
            yield return Delta("Tool results: " + string.Join("; ", request.PendingToolResults.Select(x => x.Content)));
            yield break;
        }
        if ((request.LatestUserMessage?.Content ?? string.Empty).Contains("mutate", StringComparison.OrdinalIgnoreCase))
        {
            var invocation = new AgentToolInvocation("delegated-call-1", DeterministicAgentProvider.MutatingToolName, request.SessionId, request.ActorId,
                new Dictionary<string, object?> { ["change"] = "add-activity" }, request.Context)
            { TenantId = request.TenantId, TurnId = request.TurnId, Client = request.Client };
            var dispatch = await request.ToolInvoker!.DispatchAsync(invocation, request.Policy, cancellationToken);
            if (dispatch.Outcome == AgentToolDispatchOutcome.ProposalCreated && dispatch.Proposal is { } proposal)
            {
                yield return new(Core.Services.AgentProviderPrimitives.NewId(), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow,
                    AgentResultKind.Proposal, proposal);
                yield break;
            }
            yield return Delta(dispatch.Result?.Content ?? dispatch.Error?.Message ?? "The change was refused.");
            yield break;
        }
        yield return Delta("Delegated agent provider response.");
    }

    public IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("A delegated provider's turn runs through its harness entry point.");

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, "Reviewed operations are decided through the server's review."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => new DeterministicAgentProvider().GetDiagnosticsAsync(cancellationToken);

    private static AgentStreamEvent Delta(string content)
        => new(Core.Services.AgentProviderPrimitives.NewId(), AgentStreamEventKind.MessageDelta, content, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
}

/// <summary>
/// The harness clock: time moves only when a test advances it, and timers never fire on their own, so background lease
/// renewal runs only when a test calls it. Timer creations are counted.
/// </summary>
internal sealed class RestartTestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private int _timers;

    public int Timers => Volatile.Read(ref _timers);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return new InertTimer();
    }

    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
