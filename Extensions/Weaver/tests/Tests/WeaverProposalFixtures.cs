using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Events;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Weaver.Tests;

// Shared proposal fixtures; the durable EF suite compiles them with the restart scenarios.
/// <summary>A replaced session store whose host owns policy changes outside the session policy claim model.</summary>
internal sealed class HostPolicySessions(IWeaverSessionService inner) : IWeaverSessionService
{
    public WeaverPolicy? Override { get; set; }
    public Task<WeaverSession> CreateAsync(WeaverSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
    public async Task<WeaverSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await inner.FindAsync(sessionId, cancellationToken);
        return session is not null && Override is { } policy ? session with { Policy = policy } : session;
    }
    public Task<WeaverMessage> AddMessageAsync(string sessionId, WeaverMessageCreateRequest request, CancellationToken cancellationToken = default) => inner.AddMessageAsync(sessionId, request, cancellationToken);
    public Task<IReadOnlyCollection<WeaverMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListMessagesAsync(sessionId, cancellationToken);
    public Task<WeaverMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => inner.FindMessageAsync(sessionId, messageId, cancellationToken);
    public Task<WeaverMessage?> FindLatestMessageAsync(string sessionId, WeaverRole? role = null, CancellationToken cancellationToken = default) => inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
    public Task SetMessageStatusAsync(string sessionId, string messageId, WeaverMessageStatus status, CancellationToken cancellationToken = default) => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
    public Task AddContextAsync(string sessionId, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.AddContextAsync(sessionId, attachments, cancellationToken);
    public Task<IReadOnlyCollection<WeaverContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListContextAsync(sessionId, cancellationToken);
}

internal sealed class ProposalFixture : IDisposable
{
    public const string Actor = "actor-1";
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;
    public IServiceProvider Services => _scope.ServiceProvider;
    public WeaverSession Session { get; private set; } = null!;
    public ProposalTestAuthorizer Authority { get; } = new();
    public ProposalTestClock Clock { get; } = new();
    public IWeaverTool Tool { get; }
    public ProposalMutationTool? Mutation => Tool as ProposalMutationTool;
    public ProposalClientTool? Client => Tool as ProposalClientTool;
    public IWeaverProposalService Proposals => _scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
    public IWeaverToolInvoker Invoker => _scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>();
    public IWeaverAuditReader Audit => _scope.ServiceProvider.GetRequiredService<IWeaverAuditReader>();
    public IWeaverFeedbackService Feedback => _scope.ServiceProvider.GetRequiredService<IWeaverFeedbackService>();

    private ProposalFixture(IWeaverTool tool, Action<IServiceCollection>? configure)
    {
        Tool = tool;
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        WeaverTestServices.AddToolResultSource(services);
        services.AddScoped<IWeaverTool>(_ => tool);
        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => Authority));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        configure?.Invoke(services);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateScope();
    }

    public static async Task<ProposalFixture> CreateAsync(IWeaverTool tool, WeaverPolicy? policy = null, Action<IServiceCollection>? configure = null)
    {
        var fixture = new ProposalFixture(tool, configure);
        fixture.Session = await fixture._scope.ServiceProvider.GetRequiredService<IWeaverSessionService>().CreateAsync(new(
            "tenant-1", Actor, "conversation-1", "test-provider", "explain", null, policy ?? WeaverPolicy.Default, new Dictionary<string, string>()));
        return fixture;
    }

    public WeaverToolInvocation Invocation(string callId = "call-1", IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var binding = new WeaverClientBinding("client-1", "generation-1");
        IReadOnlyCollection<WeaverContextAttachment> context = Tool.Descriptor.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState
            ? [new("snapshot-1", "test", "editor-1", "Editor", "test.editor", WeaverContextSensitivity.Internal, "selection", "Bound editor", null, new Dictionary<string, string>())
            { ModuleId = "test.module", Origin = WeaverContextOrigin.ClientSnapshot, Client = binding, Target = new("test.module", "test.editor", "editor-1", "revision-1") }]
            : [];
        return new(callId, Tool.Descriptor.Name, Session.Id, Actor,
            arguments ?? new Dictionary<string, object?> { ["value"] = "new", ["text"] = "hi" }, context)
        { TenantId = Session.TenantId, TurnId = "turn-1", Client = binding };
    }

    public async Task<WeaverActionProposal> ProposeAsync()
    {
        var dispatch = await Invoker.DispatchAsync(Invocation(), Session.Policy);
        Assert.Equal(WeaverToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        return dispatch.Proposal!;
    }
    public Task<WeaverResult<WeaverActionProposal>> ApproveAsync(WeaverActionProposal proposal) => Proposals.ApproveAsync(proposal.Id, Actor, proposal.BaseRevision);
    public Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal) => Proposals.ExecuteAsync(proposal.Id, Actor, proposal.BaseRevision);
    public WeaverClientAcknowledgement Acknowledgement(WeaverActionProposal proposal) => new(proposal.PreparedInvocation!.Client!, proposal.PreparedInvocation.ApprovalDigest,
        WeaverClientOutcome.Applied, "The client observed the edit.", proposal.ResourceTarget! with { Revision = "revision-2" },
        new("test.client-change", "1", JsonSerializer.SerializeToElement(new { observed = true, value = "new", revision = "revision-2", secret = "omit" })));
    public Task<WeaverResult<WeaverProposalExecutionResult>> AcknowledgeAsync(WeaverActionProposal proposal, WeaverClientAcknowledgement? acknowledgement = null)
        => Proposals.AcknowledgeAsync(Session.Id, proposal.PreparedInvocation!.InvocationId, Actor, acknowledgement ?? Acknowledgement(proposal));
    public void Dispose() { _scope.Dispose(); _root.Dispose(); }
}

internal sealed class ProposalTestAuthorizer : IWeaverToolAuthorizer
{
    private Func<CancellationToken, Task>? _beforeNextDecision;
    public bool Allowed { get; set; } = true;

    /// <summary>A one-shot barrier that runs before the next current-authority decision.</summary>
    public void HoldNextDecision(Func<CancellationToken, Task> barrier) => Volatile.Write(ref _beforeNextDecision, barrier);

    public async Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _beforeNextDecision, null) is { } barrier) await barrier(cancellationToken);
        return new WeaverPolicyDecision(Allowed, Allowed ? [] : [new("test.denied", "Denied")]);
    }
}

internal sealed class ProposalTestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}

internal sealed class MutableProposalToolRegistry(IWeaverTool tool) : IWeaverToolRegistry
{
    public IWeaverTool? Current { get; set; } = tool;
    public Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<IWeaverTool>>(Current is null ? [] : [Current]);
    public Task<IWeaverTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(Current?.Descriptor.Name, toolName, StringComparison.OrdinalIgnoreCase) ? Current : null);
}

internal sealed class ProposalClientToolWithoutCompletion(ProposalClientTool tool) : IWeaverTool
{
    public int PrepareCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public WeaverToolDescriptor Descriptor => tool.Descriptor;
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        return tool.PrepareAsync(invocation, cancellationToken);
    }
    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return tool.ExecuteAsync(invocation, cancellationToken);
    }
}

internal sealed class TemporarilyUnavailableCompletionExecutor(DefaultWeaverActionProposalExecutor inner) : IWeaverActionProposalExecutor
{
    public bool Unavailable { get; set; } = true;
    public int ExecuteCalls { get; private set; }
    public Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return inner.ExecuteAsync(proposal, cancellationToken);
    }
    public Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal,
        WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        => Unavailable
            ? Task.FromResult(WeaverResult<WeaverProposalExecutionResult>.Failure("test.completion_unavailable", "Completion validation is temporarily unavailable.", 409))
            : inner.CompleteClientAsync(proposal, acknowledgement, cancellationToken);
}

internal sealed class ProposalMutationTool : IWeaverTool
{
    private int _revision = 1;
    public string Value { get; private set; } = "initial";
    public string Revision => $"revision-{_revision}";
    public int PrepareCalls { get; private set; }
    /// <summary>Owning-module dispatches, counted before any effect or barrier.</summary>
    public int DispatchCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public WeaverRisk CurrentRisk { get; set; } = WeaverRisk.ReviewRequired;
    public bool NoopWithoutReceipt { get; init; }
    public bool FailAfterEffect { get; init; }
    public bool CancelAfterEffect { get; init; }
    public bool Malformed { get; init; }
    /// <summary>After dispatch, refuses with this module-authored failure code (for example the engine's cancellation code).</summary>
    public string? RefuseAfterDispatchWith { get; init; }
    public TaskCompletionSource? Started { get; init; }
    public TaskCompletionSource? Release { get; init; }
    public WeaverToolDescriptor Descriptor => new("test_mutate", "Update resource", "Updates one owned test resource.", WeaverToolMutability.Mutating,
        CurrentRisk, "{}", [], []) { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId,
        ResultType = "test.change", ResultSchemaVersion = "1" };
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        if (Malformed) return Task.FromResult(WeaverResult<WeaverToolPreparation>.Failure("test.malformed", "The module refused malformed arguments."));
        return Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(new(new("test.module", "test.resource", "resource-1", Revision),
            JsonSerializer.SerializeToElement(invocation.Arguments), WeaverRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { before = Value, after = "new" })), false)));
    }
    public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        DispatchCalls++;
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Target!.Revision != Revision) return WeaverResult<WeaverToolResult>.Failure("test.stale", "The resource changed after review.", 409);
        Started?.TrySetResult();
        if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
        if (RefuseAfterDispatchWith is { } code) return WeaverResult<WeaverToolResult>.Failure(code, "The module refused after dispatch.", 409);
        ExecuteCalls++;
        if (NoopWithoutReceipt) return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "A misleading success."));
        Value = invocation.Arguments.GetProperty("value").GetString()!;
        _revision++;
        if (FailAfterEffect) throw new InvalidOperationException("secret-test-value");
        if (CancelAfterEffect) throw new OperationCanceledException("secret-test-value");
        var readback = new WeaverPayload("test.change", "1", JsonSerializer.SerializeToElement(new { value = Value, revision = Revision }));
        return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "The owning resource was updated.", readback)
        { Receipt = new(invocation.InvocationId, invocation.Target, invocation.Target with { Revision = Revision }, WeaverEffectScope.ServerPersisted, readback) });
    }
    public void ChangeByUser(string value) { Value = value; _revision++; }
}

internal sealed class ProposalClientTool : IWeaverTool, IWeaverClientTool
{
    public int ExecuteCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public bool UseSnapshotBinding { get; set; }
    public TaskCompletionSource? CompleteStarted { get; init; }
    public TaskCompletionSource? CompleteRelease { get; init; }
    public WeaverToolDescriptor Descriptor { get; set; } = new("test_client_change", "Change local resource", "Changes an originating local resource.", WeaverToolMutability.Mutating,
        WeaverRisk.ReviewRequired, "{}", [], []) { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId,
        ExecutionLocation = WeaverExecutionLocation.ClientWorkingState, ResultType = "test.client-change", ResultSchemaVersion = "1" };
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(new(new("test.module", "test.editor", "editor-1", "revision-1"),
            JsonSerializer.SerializeToElement(invocation.Arguments), WeaverRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { value = "new" })), false)
        { Client = UseSnapshotBinding ? invocation.Context.Single().Client : invocation.Client }));
    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    { ExecuteCalls++; return Task.FromResult(WeaverResult<WeaverToolResult>.Failure("test.client_only", "The browser owns this operation.")); }
    public async Task<WeaverResult<WeaverToolResult>> CompleteAsync(WeaverPreparedToolInvocation invocation, WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        CompleteCalls++;
        CompleteStarted?.TrySetResult();
        if (CompleteRelease is not null) await CompleteRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return Complete(invocation, acknowledgement);
    }

    private WeaverResult<WeaverToolResult> Complete(WeaverPreparedToolInvocation invocation, WeaverClientAcknowledgement acknowledgement)
    {
        if (acknowledgement.After is not { } after || after.ModuleId != invocation.ModuleId || after.ResourceId != invocation.Target!.ResourceId
            || acknowledgement.Result is not { TypeId: "test.client-change", SchemaVersion: "1" } payload || !payload.Data.GetProperty("observed").GetBoolean())
            return WeaverResult<WeaverToolResult>.Failure("test.observation_invalid", "No valid observed outcome.", 409);
        var admitted = new WeaverPayload("test.client-change", "1", JsonSerializer.SerializeToElement(new
        { value = payload.Data.GetProperty("value").GetString(), revision = after.Revision, observed = true }));
        return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Observed local working-state edit.", admitted)
        { Receipt = new(invocation.InvocationId, invocation.Target!, after, WeaverEffectScope.ClientWorkingState, admitted) });
    }
}

/// <summary>Forwards to the real audit store; a one-shot hook can run after the next event of one kind.</summary>
internal sealed class HookedAuditSink : IWeaverAuditSink
{
    private readonly object _gate = new();
    private IWeaverAuditSink _inner = null!;
    private (WeaverAuditEventKind Kind, Func<WeaverAuditEvent, CancellationToken, Task> Hook)? _next;

    public HookedAuditSink Bind(IWeaverAuditSink inner)
    {
        _inner = inner;
        return this;
    }

    public void OnNext(WeaverAuditEventKind kind, Func<WeaverAuditEvent, CancellationToken, Task> hook)
    {
        lock (_gate) _next = (kind, hook);
    }

    /// <summary>Holds the emitting transition, after its state write and inside its proposal gate, until released.</summary>
    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext(WeaverAuditEventKind kind)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnNext(kind, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        return (entered, release);
    }

    public async Task EmitAsync(WeaverAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _inner.EmitAsync(auditEvent, cancellationToken);
        Func<WeaverAuditEvent, CancellationToken, Task>? hook = null;
        lock (_gate)
            if (_next is { } next && next.Kind == auditEvent.Kind)
            {
                hook = next.Hook;
                _next = null;
            }
        if (hook is not null) await hook(auditEvent, cancellationToken);
    }
}
