using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 T009: simulates host restarts. Each <see cref="RecoveryHost"/> is a complete default composition; a restart
/// disposes it and builds a new one over the same round-tripping record store, the same clock and the same owning
/// module, whose execution count is the authority on how often an effect ran. Only the store is shared: every other
/// in-memory object, including the proposal store and claims, belongs to one host and is lost on restart.
/// </summary>
internal sealed class WeaverRestartHarness : IDisposable
{
    /// <summary>
    /// The store every harness builds when a test names none: the round-tripping in-memory store, unless a test assembly
    /// sets another once at load (the durable EF suite runs these same scenarios over SQLite this way).
    /// </summary>
    internal static Func<TimeProvider, IWeaverSessionRecordStore>? DefaultStore { get; set; }

    /// <summary>Releases a store <see cref="DefaultStore"/> built (its database, for example) when its harness ends.</summary>
    internal static Action<IWeaverSessionRecordStore>? ReleaseDefaultStore { get; set; }

    /// <param name="store">Builds the shared store over the harness clock; the default is <see cref="DefaultStore"/>.</param>
    /// <param name="delegated">Runs turns through a delegated (harness-owned) loop, as the Copilot adapter does, instead of the host-driven loop.</param>
    public WeaverRestartHarness(Func<TimeProvider, IWeaverSessionRecordStore>? store = null, Action<IServiceCollection>? configure = null, bool delegated = false)
    {
        Provider = delegated ? new HeldDelegatedProvider() : new HeldTurnProvider();
        if (store is null && DefaultStore is { } defaultStore)
        {
            Store = defaultStore(Clock);
            _ownsDefaultStore = true;
        }
        else Store = store?.Invoke(Clock) ?? new InMemoryWeaverSessionRecordStore(Clock);
        _configure = configure;
        Host = new RecoveryHost(this);
    }

    private readonly Action<IServiceCollection>? _configure;
    private readonly bool _ownsDefaultStore;

    public RestartTestClock Clock { get; } = new();
    public IWeaverSessionRecordStore Store { get; }
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
        if (afterLease) Clock.Advance(InMemoryWeaverSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));
        return Host = new RecoveryHost(this);
    }

    /// <summary>A second, concurrently running host over the same store, clock and module (another process).</summary>
    public RecoveryHost StartAnotherHost() => new(this);

    internal void Configure(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        services.Replace(ServiceDescriptor.Singleton(Store));
        services.AddSingleton<IWeaverProvider>(Provider);
        services.AddSingleton<IWeaverTool>(Module);
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

    public RecoveryHost(WeaverRestartHarness harness)
    {
        _root = WeaverTestServices.Create(harness.Configure);
        _scope = _root.CreateScope();
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public IWeaverSessionService Sessions => Services.GetRequiredService<IWeaverSessionService>();
    public InMemoryWeaverSessionService Owner => (InMemoryWeaverSessionService)Sessions;
    public IWeaverStreamingService Streaming => Services.GetRequiredService<IWeaverStreamingService>();
    public IWeaverProposalService Proposals => Services.GetRequiredService<IWeaverProposalService>();
    public IWeaverToolInvoker Invoker => Services.GetRequiredService<IWeaverToolInvoker>();
    public IWeaverSessionPolicyService Policy => Services.GetRequiredService<IWeaverSessionPolicyService>();
    public IWeaverAuditReader Audit => Services.GetRequiredService<IWeaverAuditReader>();
    public IWeaverTurnStateStore Pauses => Services.GetRequiredService<IWeaverTurnStateStore>();
    public InMemoryWeaverProposalStore ProposalStore => Services.GetRequiredService<InMemoryWeaverProposalStore>();

    public Task<WeaverSession> CreateAsync(WeaverPolicy? policy = null) => Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1",
        DeterministicWeaverProvider.Id, "explain", "Recovery session", policy ?? WeaverPolicy.Default, new Dictionary<string, string>()));

    public Task<WeaverMessage> PostAsync(WeaverSession session, string content, string? requestId = null, string? fingerprint = null)
        => Sessions.AddMessageAsync(session.Id, new(WeaverRole.User, content, WeaverMessageStatus.Pending, null, [], [])
        { RequestId = requestId, DeliveryFingerprint = fingerprint });

    public async Task<List<WeaverStreamEvent>> StreamAsync(WeaverSession session, CancellationToken cancellationToken = default)
    {
        var events = new List<WeaverStreamEvent>();
        await foreach (var item in Streaming.StreamAsync(session.Id, cancellationToken))
            events.Add(item);
        return events;
    }

    /// <summary>Prompts a mutation and runs the turn until it pauses on the proposal for review.</summary>
    public async Task<WeaverActionProposal> PauseOnReviewAsync(WeaverSession session)
    {
        await PostAsync(session, "mutate the resource");
        var events = await StreamAsync(session);
        return Assert.IsType<WeaverActionProposal>(Assert.Single(events, x => x.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
    }

    public Task<WeaverSessionPolicyResult> ProbeAsync(WeaverSession session, long revision = 1)
        => Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, WeaverAutonomyMode.AutoReadOnly, revision));

    public async Task<IReadOnlyList<WeaverAuditEvent>> AuditAsync(WeaverSession session) => [.. await Audit.ListAsync(session.Id)];

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
    }
}

/// <summary>The owning module: a reviewed server mutation whose dispatches are counted across every host.</summary>
internal sealed class RecoveryModuleTool : IWeaverTool
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
    public WeaverRisk Risk { get; set; } = WeaverRisk.ReviewRequired;
    /// <summary>The permissions the current principal needs to apply the change; none by default.</summary>
    public IReadOnlyCollection<string> RequiredPermissions { get; set; } = [];

    public WeaverToolDescriptor Descriptor => new(DeterministicWeaverProvider.MutatingToolName, "Apply change", "Applies a test resource change.",
        WeaverToolMutability.Mutating, Risk, """{"type":"object","properties":{"change":{"type":"string"}}}""", RequiredPermissions, [])
    { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ResultType = "test.change", ResultSchemaVersion = "1" };

    /// <summary>A user edit in the owning module moves the resource to a new revision.</summary>
    public void ChangeByUser() => Revision += "-edited";

    /// <summary>Holds the next dispatch after it is counted and before it reports anything.</summary>
    public (TaskCompletionSource Started, TaskCompletionSource Release) HoldNextDispatch()
        => (_hold = (new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously))).Value;

    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _preparations);
        return Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor) with
        { Target = new("test.module", "test.resource", "resource-1", Revision) }));
    }

    public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _dispatches);
        if (_hold is { } hold)
        {
            _hold = null;
            hold.Started.TrySetResult();
            await hold.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        if (invocation.Target?.Revision != Revision)
            return WeaverResult<WeaverToolResult>.Failure("test.revision_changed", "The resource moved since preparation.", 409);
        Revision = $"revision-{count + 1}";
        Interlocked.Increment(ref _effects);
        var data = new WeaverPayload("test.change", "1", JsonSerializer.SerializeToElement(new { dispatches = count }));
        return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "applied", data)
        { Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = Revision }, WeaverEffectScope.ServerPersisted, data) });
    }
}

/// <summary>What a provider was given for one turn, on either loop.</summary>
internal sealed record RecordedTurn(IReadOnlyList<WeaverTurnMessage> History, IReadOnlyList<WeaverToolResult> PendingToolResults);

/// <summary>A recovery test provider: its next turn can be held mid-provider, and every turn's input is recorded.</summary>
internal interface IRecoveryTestProvider : IWeaverProvider
{
    int Turns { get; }
    List<RecordedTurn> Contexts { get; }
    (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext();
}

/// <summary>The deterministic host-driven provider; its next turn can be held mid-provider, and every context is recorded.</summary>
internal sealed class HeldTurnProvider : IRecoveryTestProvider
{
    private readonly DeterministicWeaverProvider _inner = new();
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

    public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);

    public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            yield return new(Core.Services.WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.MessageDelta, "Partial ", null, null, DateTimeOffset.UtcNow, WeaverResultKind.Message);
            held.Entered.TrySetResult();
            await held.Release.Task.WaitAsync(cancellationToken);
        }
        await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken))
            yield return item;
    }

    public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
    public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
}

/// <summary>
/// The deterministic delegated provider: it owns the turn's loop as the Copilot adapter does, dispatching the same
/// reviewed mutation through the server's tool invoker and reporting a created proposal as a stream event, and it receives
/// a recorded receipt as a continuation. Its next turn can be held mid-provider, and every request is recorded.
/// </summary>
internal sealed class HeldDelegatedProvider : IRecoveryTestProvider, IWeaverHarness
{
    private readonly object _gate = new();
    private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;
    private int _turns;

    public string ProviderId => DeterministicWeaverProvider.Id;
    public WeaverHarnessCapabilities Capabilities => WeaverHarnessCapabilities.Tools;
    public int Turns => Volatile.Read(ref _turns);
    public List<RecordedTurn> Contexts { get; } = [];

    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
    {
        var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_gate) _next = hold;
        return hold;
    }

    public async IAsyncEnumerable<WeaverStreamEvent> RunTurnAsync(WeaverHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            var invocation = new WeaverToolInvocation("delegated-call-1", DeterministicWeaverProvider.MutatingToolName, request.SessionId, request.ActorId,
                new Dictionary<string, object?> { ["change"] = "add-activity" }, request.Context)
            { TenantId = request.TenantId, TurnId = request.TurnId, Client = request.Client };
            var dispatch = await request.ToolInvoker!.DispatchAsync(invocation, request.Policy, cancellationToken);
            if (dispatch.Outcome == WeaverToolDispatchOutcome.ProposalCreated && dispatch.Proposal is { } proposal)
            {
                yield return new(Core.Services.WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow,
                    WeaverResultKind.Proposal, proposal);
                yield break;
            }
            yield return Delta(dispatch.Result?.Content ?? dispatch.Error?.Message ?? "The change was refused.");
            yield break;
        }
        yield return Delta("Delegated agent provider response.");
    }

    public IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("A delegated provider's turn runs through its harness entry point.");

    public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

    public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverToolApprovalResult(false, "Reviewed operations are decided through the server's review."));

    public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => new DeterministicWeaverProvider().GetDiagnosticsAsync(cancellationToken);

    private static WeaverStreamEvent Delta(string content)
        => new(Core.Services.WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.MessageDelta, content, null, null, DateTimeOffset.UtcNow, WeaverResultKind.Message);
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
