using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Events;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

// Shared proposal fixtures; the durable EF suite compiles them with the restart scenarios.
/// <summary>A replaced session store whose host owns policy changes outside the session policy claim model.</summary>
internal sealed class HostPolicySessions(IAgentSessionService inner) : IAgentSessionService
{
    public AgentPolicy? Override { get; set; }
    public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
    public async Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await inner.FindAsync(sessionId, cancellationToken);
        return session is not null && Override is { } policy ? session with { Policy = policy } : session;
    }
    public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) => inner.AddMessageAsync(sessionId, request, cancellationToken);
    public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListMessagesAsync(sessionId, cancellationToken);
    public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => inner.FindMessageAsync(sessionId, messageId, cancellationToken);
    public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) => inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
    public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default) => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
    public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.AddContextAsync(sessionId, attachments, cancellationToken);
    public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListContextAsync(sessionId, cancellationToken);
}

internal sealed class ProposalFixture : IDisposable
{
    public const string Actor = "actor-1";
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;
    public IServiceProvider Services => _scope.ServiceProvider;
    public AgentSession Session { get; private set; } = null!;
    public ProposalTestAuthorizer Authority { get; } = new();
    public ProposalTestClock Clock { get; } = new();
    public IAgentTool Tool { get; }
    public ProposalMutationTool? Mutation => Tool as ProposalMutationTool;
    public ProposalClientTool? Client => Tool as ProposalClientTool;
    public IAgentProposalService Proposals => _scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
    public IAgentToolInvoker Invoker => _scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>();
    public IAgentAuditReader Audit => _scope.ServiceProvider.GetRequiredService<IAgentAuditReader>();
    public IAgentFeedbackService Feedback => _scope.ServiceProvider.GetRequiredService<IAgentFeedbackService>();

    private ProposalFixture(IAgentTool tool, Action<IServiceCollection>? configure)
    {
        Tool = tool;
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        AgentTestServices.AddToolResultSource(services);
        services.AddScoped<IAgentTool>(_ => tool);
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => Authority));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        configure?.Invoke(services);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateScope();
    }

    public static async Task<ProposalFixture> CreateAsync(IAgentTool tool, AgentPolicy? policy = null, Action<IServiceCollection>? configure = null)
    {
        var fixture = new ProposalFixture(tool, configure);
        fixture.Session = await fixture._scope.ServiceProvider.GetRequiredService<IAgentSessionService>().CreateAsync(new(
            "tenant-1", Actor, "conversation-1", "test-provider", "explain", null, policy ?? AgentPolicy.Default, new Dictionary<string, string>()));
        return fixture;
    }

    public AgentToolInvocation Invocation(string callId = "call-1", IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var binding = new AgentClientBinding("client-1", "generation-1");
        IReadOnlyCollection<AgentContextAttachment> context = Tool.Descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState
            ? [new("snapshot-1", "test", "editor-1", "Editor", "test.editor", AgentContextSensitivity.Internal, "selection", "Bound editor", null, new Dictionary<string, string>())
            { ModuleId = "test.module", Origin = AgentContextOrigin.ClientSnapshot, Client = binding, Target = new("test.module", "test.editor", "editor-1", "revision-1") }]
            : [];
        return new(callId, Tool.Descriptor.Name, Session.Id, Actor,
            arguments ?? new Dictionary<string, object?> { ["value"] = "new", ["text"] = "hi" }, context)
        { TenantId = Session.TenantId, TurnId = "turn-1", Client = binding };
    }

    public async Task<AgentActionProposal> ProposeAsync()
    {
        var dispatch = await Invoker.DispatchAsync(Invocation(), Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        return dispatch.Proposal!;
    }
    public Task<AgentResult<AgentActionProposal>> ApproveAsync(AgentActionProposal proposal) => Proposals.ApproveAsync(proposal.Id, Actor, proposal.BaseRevision);
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal) => Proposals.ExecuteAsync(proposal.Id, Actor, proposal.BaseRevision);
    public AgentClientAcknowledgement Acknowledgement(AgentActionProposal proposal) => new(proposal.PreparedInvocation!.Client!, proposal.PreparedInvocation.ApprovalDigest,
        AgentClientOutcome.Applied, "The client observed the edit.", proposal.ResourceTarget! with { Revision = "revision-2" },
        new("test.client-change", "1", JsonSerializer.SerializeToElement(new { observed = true, value = "new", revision = "revision-2", secret = "omit" })));
    public Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(AgentActionProposal proposal, AgentClientAcknowledgement? acknowledgement = null)
        => Proposals.AcknowledgeAsync(Session.Id, proposal.PreparedInvocation!.InvocationId, Actor, acknowledgement ?? Acknowledgement(proposal));
    public void Dispose() { _scope.Dispose(); _root.Dispose(); }
}

internal sealed class ProposalTestAuthorizer : IAgentToolAuthorizer
{
    private Func<CancellationToken, Task>? _beforeNextDecision;
    public bool Allowed { get; set; } = true;

    /// <summary>A one-shot barrier that runs before the next current-authority decision.</summary>
    public void HoldNextDecision(Func<CancellationToken, Task> barrier) => Volatile.Write(ref _beforeNextDecision, barrier);

    public async Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _beforeNextDecision, null) is { } barrier) await barrier(cancellationToken);
        return new AgentPolicyDecision(Allowed, Allowed ? [] : [new("test.denied", "Denied")]);
    }
}

internal sealed class ProposalTestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}

internal sealed class MutableProposalToolRegistry(IAgentTool tool) : IAgentToolRegistry
{
    public IAgentTool? Current { get; set; } = tool;
    public Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<IAgentTool>>(Current is null ? [] : [Current]);
    public Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(Current?.Descriptor.Name, toolName, StringComparison.OrdinalIgnoreCase) ? Current : null);
}

internal sealed class ProposalClientToolWithoutCompletion(ProposalClientTool tool) : IAgentTool
{
    public int PrepareCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public AgentToolDescriptor Descriptor => tool.Descriptor;
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        return tool.PrepareAsync(invocation, cancellationToken);
    }
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return tool.ExecuteAsync(invocation, cancellationToken);
    }
}

internal sealed class TemporarilyUnavailableCompletionExecutor(DefaultAgentActionProposalExecutor inner) : IAgentActionProposalExecutor
{
    public bool Unavailable { get; set; } = true;
    public int ExecuteCalls { get; private set; }
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return inner.ExecuteAsync(proposal, cancellationToken);
    }
    public Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        => Unavailable
            ? Task.FromResult(AgentResult<AgentProposalExecutionResult>.Failure("test.completion_unavailable", "Completion validation is temporarily unavailable.", 409))
            : inner.CompleteClientAsync(proposal, acknowledgement, cancellationToken);
}

internal sealed class ProposalMutationTool : IAgentTool
{
    private int _revision = 1;
    public string Value { get; private set; } = "initial";
    public string Revision => $"revision-{_revision}";
    public int PrepareCalls { get; private set; }
    /// <summary>Owning-module dispatches, counted before any effect or barrier.</summary>
    public int DispatchCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public AgentRisk CurrentRisk { get; set; } = AgentRisk.ReviewRequired;
    public bool NoopWithoutReceipt { get; init; }
    public bool FailAfterEffect { get; init; }
    public bool CancelAfterEffect { get; init; }
    public bool Malformed { get; init; }
    /// <summary>After dispatch, refuses with this module-authored failure code (for example the engine's cancellation code).</summary>
    public string? RefuseAfterDispatchWith { get; init; }
    public TaskCompletionSource? Started { get; init; }
    public TaskCompletionSource? Release { get; init; }
    public AgentToolDescriptor Descriptor => new("test_mutate", "Update resource", "Updates one owned test resource.", AgentToolMutability.Mutating,
        CurrentRisk, "{}", [], []) { ModuleId = "test.module", DisclosureSourceId = AgentTestContextSource.TestModuleSourceId,
        ResultType = "test.change", ResultSchemaVersion = "1" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        if (Malformed) return Task.FromResult(AgentResult<AgentToolPreparation>.Failure("test.malformed", "The module refused malformed arguments."));
        return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(new("test.module", "test.resource", "resource-1", Revision),
            JsonSerializer.SerializeToElement(invocation.Arguments), AgentRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { before = Value, after = "new" })), false)));
    }
    public async Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        DispatchCalls++;
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Target!.Revision != Revision) return AgentResult<AgentToolResult>.Failure("test.stale", "The resource changed after review.", 409);
        Started?.TrySetResult();
        if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
        if (RefuseAfterDispatchWith is { } code) return AgentResult<AgentToolResult>.Failure(code, "The module refused after dispatch.", 409);
        ExecuteCalls++;
        if (NoopWithoutReceipt) return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "A misleading success."));
        Value = invocation.Arguments.GetProperty("value").GetString()!;
        _revision++;
        if (FailAfterEffect) throw new InvalidOperationException("secret-test-value");
        if (CancelAfterEffect) throw new OperationCanceledException("secret-test-value");
        var readback = new AgentPayload("test.change", "1", JsonSerializer.SerializeToElement(new { value = Value, revision = Revision }));
        return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "The owning resource was updated.", readback)
        { Receipt = new(invocation.InvocationId, invocation.Target, invocation.Target with { Revision = Revision }, AgentEffectScope.ServerPersisted, readback) });
    }
    public void ChangeByUser(string value) { Value = value; _revision++; }
}

internal sealed class ProposalClientTool : IAgentTool, IAgentClientTool
{
    public int ExecuteCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public bool UseSnapshotBinding { get; set; }
    public TaskCompletionSource? CompleteStarted { get; init; }
    public TaskCompletionSource? CompleteRelease { get; init; }
    public AgentToolDescriptor Descriptor { get; set; } = new("test_client_change", "Change local resource", "Changes an originating local resource.", AgentToolMutability.Mutating,
        AgentRisk.ReviewRequired, "{}", [], []) { ModuleId = "test.module", DisclosureSourceId = AgentTestContextSource.TestModuleSourceId,
        ExecutionLocation = AgentExecutionLocation.ClientWorkingState, ResultType = "test.client-change", ResultSchemaVersion = "1" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(new("test.module", "test.editor", "editor-1", "revision-1"),
            JsonSerializer.SerializeToElement(invocation.Arguments), AgentRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { value = "new" })), false)
        { Client = UseSnapshotBinding ? invocation.Context.Single().Client : invocation.Client }));
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    { ExecuteCalls++; return Task.FromResult(AgentResult<AgentToolResult>.Failure("test.client_only", "The browser owns this operation.")); }
    public async Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        CompleteCalls++;
        CompleteStarted?.TrySetResult();
        if (CompleteRelease is not null) await CompleteRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return Complete(invocation, acknowledgement);
    }

    private AgentResult<AgentToolResult> Complete(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement)
    {
        if (acknowledgement.After is not { } after || after.ModuleId != invocation.ModuleId || after.ResourceId != invocation.Target!.ResourceId
            || acknowledgement.Result is not { TypeId: "test.client-change", SchemaVersion: "1" } payload || !payload.Data.GetProperty("observed").GetBoolean())
            return AgentResult<AgentToolResult>.Failure("test.observation_invalid", "No valid observed outcome.", 409);
        var admitted = new AgentPayload("test.client-change", "1", JsonSerializer.SerializeToElement(new
        { value = payload.Data.GetProperty("value").GetString(), revision = after.Revision, observed = true }));
        return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Observed local working-state edit.", admitted)
        { Receipt = new(invocation.InvocationId, invocation.Target!, after, AgentEffectScope.ClientWorkingState, admitted) });
    }
}

/// <summary>Forwards to the real audit store; a one-shot hook can run after the next event of one kind.</summary>
internal sealed class HookedAuditSink : IAgentAuditSink
{
    private readonly object _gate = new();
    private IAgentAuditSink _inner = null!;
    private (AgentAuditEventKind Kind, Func<AgentAuditEvent, CancellationToken, Task> Hook)? _next;

    public HookedAuditSink Bind(IAgentAuditSink inner)
    {
        _inner = inner;
        return this;
    }

    public void OnNext(AgentAuditEventKind kind, Func<AgentAuditEvent, CancellationToken, Task> hook)
    {
        lock (_gate) _next = (kind, hook);
    }

    /// <summary>Holds the emitting transition, after its state write and inside its proposal gate, until released.</summary>
    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext(AgentAuditEventKind kind)
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

    public async Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _inner.EmitAsync(auditEvent, cancellationToken);
        Func<AgentAuditEvent, CancellationToken, Task>? hook = null;
        lock (_gate)
            if (_next is { } next && next.Kind == auditEvent.Kind)
            {
                hook = next.Hook;
                _next = null;
            }
        if (hook is not null) await hook(auditEvent, cancellationToken);
    }
}
