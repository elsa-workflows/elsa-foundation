using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// Retained engine lifecycle outcomes (denied/refused/uncertain, no receipt) are disclosed under the module
/// owner's current read/disclosure authority, not the original mutation authority; applied effects and
/// module-authored results keep the full current tool-authority check.
/// </summary>
public sealed class AgentLifecycleOutcomeDisclosureTests
{
    [Fact]
    public async Task Refusal_after_write_withdrawal_continues_the_originating_turn_while_read_authority_remains()
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        Assert.True((await harness.Proposals(scope).ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);

        var execution = await harness.Proposals(scope).ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.False(execution.Succeeded);
        Assert.Equal(403, execution.Error!.StatusCode);
        var resumed = await Collect(scope, session.Id);

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        Assert.DoesNotContain(resumed, x => x.Kind == AgentStreamEventKind.Error);
        var delivered = Assert.Single(harness.Provider.Inputs[^1].PendingToolResults);
        AssertLifecycle(delivered, "refused", proposal);
        Assert.Equal(proposal.PreparedInvocation!.ToolCallId, delivered.ToolCallId);
        Assert.Equal(0, harness.Tool.Executions);
        Assert.True(harness.Source.ResultChecks > 0);
        var messages = await harness.Sessions(scope).ListMessagesAsync(session.Id);
        Assert.Equal(AgentMessageStatus.Completed, Assert.Single(messages, x => x.Role == AgentRole.User).Status);

        // The journaled refusal stays disclosable to later prompts under the same read-only authority.
        await harness.Sessions(scope).AddMessageAsync(session.Id, new(AgentRole.User, "thanks", AgentMessageStatus.Pending, null, [], []));
        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(scope, session.Id))[^1].Kind);
        Assert.Equal(0, harness.Tool.Executions);
    }

    [Fact]
    public async Task Denial_continues_the_originating_turn_after_write_withdrawal()
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);
        Assert.True((await harness.Proposals(scope).DenyAsync(proposal.Id, session.ActorId, "no")).Succeeded);

        var resumed = await Collect(scope, session.Id);

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        AssertLifecycle(Assert.Single(harness.Provider.Inputs[^1].PendingToolResults), "denied", proposal);
        Assert.Equal(0, harness.Tool.Executions);
    }

    [Fact]
    public async Task Uncertain_outcome_continues_the_originating_turn_after_write_withdrawal()
    {
        var harness = Harness.Create(receipt: false);
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        Assert.True((await harness.Proposals(scope).ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var execution = await harness.Proposals(scope).ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.Equal("uncertain", execution.Value?.Status);
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);

        var resumed = await Collect(scope, session.Id);

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        AssertLifecycle(Assert.Single(harness.Provider.Inputs[^1].PendingToolResults), "uncertain", proposal);
        Assert.Equal(1, harness.Tool.Executions);
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("denied")]
    [InlineData("uncertain")]
    public async Task Read_withdrawal_still_withholds_a_lifecycle_outcome_before_provider_entry(string outcome)
    {
        var harness = Harness.Create(receipt: outcome != "uncertain");
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        var proposals = harness.Proposals(scope);
        if (outcome == "denied") Assert.True((await proposals.DenyAsync(proposal.Id, session.ActorId, "no")).Succeeded);
        else
        {
            Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            if (outcome == "refused") harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);
            await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        }
        AssertLifecycle(Assert.IsType<AgentToolResult>(await proposals.FindResultAsync(proposal.Id)), outcome, proposal);
        var callsBefore = harness.Provider.Inputs.Count;
        harness.Authority.Read = false;

        var denied = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, harness.Provider.Inputs.Count);
        Assert.Equal("agent.result.disclosure_denied", Assert.Single(denied, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        Assert.Equal(AgentMessageStatus.Failed, Assert.Single(await harness.Sessions(scope).ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status);
    }

    [Fact]
    public async Task Applied_effect_after_write_withdrawal_keeps_requiring_current_tool_authority()
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        Assert.True((await harness.Proposals(scope).ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await harness.Proposals(scope).ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var stored = await harness.Proposals(scope).FindResultAsync(proposal.Id);
        Assert.NotNull(stored?.Receipt);
        var callsBefore = harness.Provider.Inputs.Count;
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);

        var denied = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, harness.Provider.Inputs.Count);
        Assert.Equal("agent.result.disclosure_denied", Assert.Single(denied, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        Assert.Equal(stored, await harness.Proposals(scope).FindResultAsync(proposal.Id));
        Assert.Equal(1, harness.Tool.Executions);
    }

    [Fact]
    public async Task A_read_tool_result_forging_the_lifecycle_shape_keeps_requiring_current_tool_authority()
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var sessions = harness.Sessions(scope);
        var session = await Session(sessions);
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "use tool", AgentMessageStatus.Pending, null, [], []));
        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(scope, session.Id))[^1].Kind);
        var forged = Assert.Single(Assert.Single(await sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Tool).ToolResults);
        Assert.False(forged.Succeeded);
        Assert.Equal("agent.operation-outcome", forged.Payload!.TypeId);
        Assert.Equal(forged.Access!.InvocationId, forged.Payload.Data.GetProperty(nameof(AgentPreparedToolInvocation.InvocationId)).GetString());
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.ReadOnlyToolName);
        var callsBefore = harness.Provider.Inputs.Count;
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "continue", AgentMessageStatus.Pending, null, [], []));

        var denied = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, harness.Provider.Inputs.Count);
        Assert.Equal("agent.result.disclosure_denied", Assert.Single(denied, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
    }

    [Theory]
    [InlineData("engine-shape", true)]
    [InlineData("invocation-mismatch", false)]
    [InlineData("extra-property", false)]
    [InlineData("applied-status", false)]
    [InlineData("foreign-type", false)]
    [InlineData("succeeded", false)]
    [InlineData("missing-invocation", false)]
    public async Task Only_the_exact_engine_lifecycle_shape_is_disclosed_without_current_mutation_authority(string variant, bool admitted)
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var sessions = harness.Sessions(scope);
        var session = await Session(sessions);
        const string invocationId = "retained-invocation";
        object data = variant switch
        {
            "invocation-mismatch" => new { status = "refused", InvocationId = "another-invocation" },
            "extra-property" => new { status = "refused", InvocationId = invocationId, detail = "owner data" },
            "applied-status" => new { status = "executed", InvocationId = invocationId },
            "missing-invocation" => new { status = "refused", invocation = invocationId },
            _ => new { status = "refused", InvocationId = invocationId }
        };
        var result = new AgentToolResult("call-1", DeterministicAgentProvider.MutatingToolName, variant == "succeeded", "Refused before application.",
            new(variant == "foreign-type" ? GatedTool.ResultType : "agent.operation-outcome", "1", JsonSerializer.SerializeToElement(data)))
        {
            Access = new(GatedTool.Module, "1", GatedTool.Target) { DisclosureSourceId = GatedSource.Id, InvocationId = invocationId }
        };
        await sessions.AddMessageAsync(session.Id, new(AgentRole.Tool, JsonSerializer.Serialize(result), AgentMessageStatus.Completed, null, [], [])
        {
            RequestId = invocationId, DeliveryFingerprint = AgentInvocationIntegrity.Digest(result), ToolResults = [result],
            HasDisclosureProvenance = true, DisclosureHistory = AgentDisclosureHistory.Freeze([new AgentDisclosureSnapshot([], [result])])
        });
        harness.Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "hello", AgentMessageStatus.Pending, null, [], []));

        var events = await Collect(scope, session.Id);

        if (admitted)
        {
            Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
            Assert.Single(harness.Provider.Inputs);
        }
        else
        {
            Assert.Empty(harness.Provider.Inputs);
            Assert.Equal("agent.result.disclosure_denied", Assert.Single(events, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        }
    }

    private static void AssertLifecycle(AgentToolResult result, string status, AgentActionProposal proposal)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Receipt);
        Assert.Equal("agent.operation-outcome", result.Payload!.TypeId);
        Assert.Equal(status, result.Payload.Data.GetProperty("status").GetString());
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, result.Payload.Data.GetProperty(nameof(AgentPreparedToolInvocation.InvocationId)).GetString());
    }

    private static Task<AgentSession> Session(IAgentSessionService sessions)
        => sessions.CreateAsync(new("tenant", "actor", "conversation", DeterministicAgentProvider.Id, "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    [Theory]
    [InlineData("refused")]
    [InlineData("denied")]
    [InlineData("uncertain")]
    public async Task An_owner_that_cannot_decide_keeps_the_paused_lifecycle_continuation_for_a_retry(string outcome)
    {
        var harness = Harness.Create(receipt: outcome != "uncertain");
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        await harness.SettleAsync(scope, session, proposal, outcome);
        var callsBefore = harness.Provider.Inputs.Count;
        var owner = Assert.IsType<InMemoryAgentSessionService>(harness.Sessions(scope));
        var pauseBefore = Assert.IsType<AgentSessionClaim>(owner.FindPause(session.Id));
        var sourceBefore = Assert.Single(await harness.Sessions(scope).ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status;
        harness.Source.Unavailable = true;

        var stopped = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, harness.Provider.Inputs.Count);
        Assert.Equal(new AgentError(GatedSource.UnavailableCode, "The owning module could not establish current authority for this operation.", 503),
            Assert.Single(stopped, x => x.Kind == AgentStreamEventKind.Error).Error);
        // The pause, its claim and its source prompt are kept: an outage is retryable, not a refusal of the continuation.
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().FindAsync(session.Id));
        Assert.Same(pauseBefore, owner.FindPause(session.Id));
        var messages = await harness.Sessions(scope).ListMessagesAsync(session.Id);
        Assert.Equal(sourceBefore, Assert.Single(messages, x => x.Role == AgentRole.User).Status);
        Assert.DoesNotContain(messages, x => x.Status == AgentMessageStatus.Failed);

        harness.Source.Unavailable = false;
        var resumed = await Collect(scope, session.Id);

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        AssertLifecycle(Assert.Single(harness.Provider.Inputs[^1].PendingToolResults), outcome, proposal);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().FindAsync(session.Id));
    }

    [Fact]
    public async Task A_denying_owner_still_releases_the_paused_lifecycle_continuation_as_refused()
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        await harness.SettleAsync(scope, session, proposal, "denied");
        harness.Authority.Read = false;

        var denied = await Collect(scope, session.Id);

        Assert.Equal(new AgentError("agent.result.disclosure_denied",
                "Current authority or host policy withholds the retained outcome details. Its accepted effect remains recorded and will not be repeated.", 403),
            Assert.Single(denied, x => x.Kind == AgentStreamEventKind.Error).Error);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().FindAsync(session.Id));
        Assert.Equal(AgentMessageStatus.Failed, Assert.Single(await harness.Sessions(scope).ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_admission_of_a_retained_lifecycle_outcome_reports_an_unavailable_owner_as_503(bool unavailable)
    {
        var harness = Harness.Create();
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        await harness.SettleAsync(scope, session, proposal, "denied");
        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(scope, session.Id))[^1].Kind);
        await harness.Sessions(scope).AddMessageAsync(session.Id, new(AgentRole.User, "and now?", AgentMessageStatus.Pending, null, [], []));
        if (unavailable) harness.Source.Unavailable = true; else harness.Authority.Read = false;

        var events = await Collect(scope, session.Id);

        var error = Assert.Single(events, x => x.Kind == AgentStreamEventKind.Error).Error;
        Assert.Equal(unavailable ? (GatedSource.UnavailableCode, 503) : ("agent.result.disclosure_denied", 403), (error?.Code, error?.StatusCode));
    }

    [Fact]
    public async Task A_host_policy_refusal_during_an_owner_outage_is_definite_and_releases_the_pause()
    {
        var policy = new PolicyGate();
        var harness = Harness.Create(configure: services => services.Replace(ServiceDescriptor.Scoped<IAgentPolicyEvaluator>(_ => policy)));
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        await harness.SettleAsync(scope, session, proposal, "denied");
        harness.Source.Unavailable = true;
        policy.Denied.Add(DeterministicAgentProvider.MutatingToolName);

        var stopped = await Collect(scope, session.Id);

        Assert.Equal(("agent.result.disclosure_denied", 403), Code(Assert.Single(stopped, x => x.Kind == AgentStreamEventKind.Error).Error));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().FindAsync(session.Id));
        Assert.Equal(AgentMessageStatus.Failed, Assert.Single(await harness.Sessions(scope).ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status);
    }

    [Fact]
    public async Task A_replacement_collector_that_rebuilds_the_unavailable_error_still_keeps_the_pause()
    {
        var harness = Harness.Create(configure: services => services.Replace(ServiceDescriptor.Scoped<IAgentContextCollector>(provider =>
            new RebuildingCollector(ActivatorUtilities.CreateInstance<DefaultAgentContextCollector>(provider)))));
        using var scope = harness.Root.CreateScope();
        var (session, proposal) = await harness.ProposeAsync(scope);
        await harness.SettleAsync(scope, session, proposal, "denied");
        harness.Source.Unavailable = true;

        var stopped = await Collect(scope, session.Id);

        Assert.Equal((GatedSource.UnavailableCode, 503), Code(Assert.Single(stopped, x => x.Kind == AgentStreamEventKind.Error).Error));
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().FindAsync(session.Id));
        Assert.DoesNotContain(await harness.Sessions(scope).ListMessagesAsync(session.Id), x => x.Status == AgentMessageStatus.Failed);
    }

    /// <summary>
    /// Across admissions the first error wins. In one full-auto turn an inline mutation retains an uncertain lifecycle
    /// outcome, then a read runs; while it runs the lifecycle outcome's owner becomes unable to decide (for historical and
    /// current admission alike) and current authority withdraws the read. The read result's admission first re-admits
    /// the recorded history in order, which reaches the earlier lifecycle outcome's outage before the later read's
    /// definite refusal, so the turn stops with the owner's 503, as the Agent catalog states.
    /// </summary>
    [Fact]
    public async Task Across_admissions_an_earlier_owner_outage_in_history_is_reported_before_a_later_definite_refusal()
    {
        var authority = new NativeAuthority();
        var source = new SplitSource();
        var provider = new SequenceProvider();
        var read = new SwitchingReadTool(() =>
        {
            source.Unavailable = true;
            authority.Withdrawn.Add(SwitchingReadTool.Name);
        });
        using var root = AgentTestServices.Create(services =>
        {
            services.AddSingleton<IAgentProvider>(provider);
            services.AddScoped<IAgentContextProvider>(_ => source);
            services.AddSingleton<IAgentTool>(new InlineMutationTool());
            services.AddSingleton<IAgentTool>(read);
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant", "actor", "conversation", provider.ProviderId, "build", null,
            AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto }, new Dictionary<string, string>()));
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "change, then read", AgentMessageStatus.Pending, null, [], []));

        var events = await Collect(scope, session.Id);

        // The inline mutation really retained a lifecycle outcome, and the read really ran into both conditions.
        var lifecycle = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted
            && x.Payload is AgentToolCallOutcome { ToolName: InlineMutationTool.Name }).Payload as AgentToolCallOutcome;
        Assert.Equal("uncertain", Assert.IsType<AgentToolResult>(lifecycle!.Result).Payload!.Data.GetProperty("status").GetString());
        Assert.Equal(1, read.Executions);
        Assert.True(source.UnavailableAnswers > 0);
        Assert.Equal((SplitSource.UnavailableCode, 503), Code(Assert.Single(events, x => x.Kind == AgentStreamEventKind.Error).Error));
    }

    private static (string?, int?) Code(AgentError? error) => (error?.Code, error?.StatusCode);

    /// <summary>A disclosure owner that, once switched, cannot decide any retained outcome, historical or current.</summary>
    private sealed class SplitSource : IAgentContextProvider
    {
        public const string Id = "split.source";
        public const string UnavailableCode = "split.authority.unavailable";
        public bool Unavailable { get; set; }
        public int UnavailableAnswers { get; private set; }
        public AgentContextProviderDescriptor Descriptor { get; } = new(Id, SplitModule, Id, [], ["1"], ["split.resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (!Unavailable || request.Results.Count == 0) return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
            UnavailableAnswers++;
            return ValueTask.FromResult(new AgentContextContribution([], [new(Id, UnavailableCode, "Owner outage.", true) { OwnerUnavailable = true }]));
        }
    }

    private const string SplitModule = "split.module";
    private static readonly AgentResourceTarget SplitTarget = new(SplitModule, "split.resource", "resource-1", "revision-1");

    /// <summary>A mutation that runs inline under full-auto and returns no receipt, so the engine retains an uncertain outcome.</summary>
    private sealed class InlineMutationTool : IAgentTool
    {
        public const string Name = "split_change";
        public AgentToolDescriptor Descriptor { get; } = new(Name, "Change", "Changes the split resource.", AgentToolMutability.Mutating,
            AgentRisk.ReviewRequired, "{}", [], []) { ModuleId = SplitModule, DisclosureSourceId = SplitSource.Id, ResultType = "split.change", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = SplitTarget }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Name, true, "applied",
                new("split.change", "1", JsonSerializer.SerializeToElement(new { size = "medium" })))));
    }

    private sealed class SwitchingReadTool(Action onExecute) : IAgentTool
    {
        public const string Name = "split_read";
        public int Executions { get; private set; }
        public AgentToolDescriptor Descriptor { get; } = new(Name, "Read", "Reads the split resource.", AgentToolMutability.ReadOnly,
            AgentRisk.ReadOnly, "{}", [], []) { ModuleId = SplitModule, DisclosureSourceId = SplitSource.Id, ResultType = "split.read", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = SplitTarget }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            onExecute();
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Name, true, "read",
                new("split.read", "1", JsonSerializer.SerializeToElement(new { size = "medium" })))));
        }
    }

    private sealed class SequenceProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider inner = new();
        public string ProviderId => inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (context.StepIndex == 0) yield return Call("split-change", InlineMutationTool.Name, AgentRisk.ReviewRequired);
            else if (context.StepIndex == 1) yield return Call("split-read", SwitchingReadTool.Name, AgentRisk.ReadOnly);
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
        private static AgentStreamEvent Call(string id, string name, AgentRisk risk)
            => new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, id, null, DateTimeOffset.UtcNow,
                Payload: new AgentToolCallRequest(id, name, "{}", risk, false));
    }

    /// <summary>Host capability policy that can withdraw a tool's capability.</summary>
    private sealed class PolicyGate : IAgentPolicyEvaluator
    {
        private readonly DefaultAgentPolicyEvaluator inner = new();
        public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
        public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default)
            => inner.EvaluateAvailabilityAsync(policy, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => inner.EvaluateContextAsync(policy, attachments, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
            => Denied.Contains(capabilityId) ? ValueTask.FromResult(new AgentPolicyDecision(false, [new("test.policy_withdrawn", "Host policy withdrew this capability.")]))
                : inner.EvaluateCapabilityAsync(policy, capabilityId, cancellationToken);
    }

    /// <summary>A supported replacement collector (a decorator here) that returns its own error instances.</summary>
    private sealed class RebuildingCollector(IAgentContextCollector inner) : IAgentContextCollector
    {
        public async Task<AgentResult<AgentContextContribution>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.CollectAsync(policy, request, cancellationToken);
            return result.Error is { } error ? new(result.Value, new AgentError(error.Code, error.Message, error.StatusCode)) : result;
        }
    }

    private static async Task<List<AgentStreamEvent>> Collect(IServiceScope scope, string sessionId)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId)) events.Add(item);
        return events;
    }

    private sealed record Harness(ServiceProvider Root, NativeAuthority Authority, GatedSource Source, GatedTool Tool, CapturingProvider Provider)
    {
        public static Harness Create(bool receipt = true, Action<IServiceCollection>? configure = null)
        {
            var authority = new NativeAuthority();
            var source = new GatedSource(authority);
            var tool = new GatedTool(receipt);
            var provider = new CapturingProvider();
            var root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentProvider>(provider);
                services.AddScoped<IAgentContextProvider>(_ => source);
                services.AddSingleton<IAgentTool>(tool);
                services.AddSingleton<IAgentTool>(new ForgingReadTool());
                services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
                configure?.Invoke(services);
            });
            return new(root, authority, source, tool, provider);
        }

        public IAgentSessionService Sessions(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        public IAgentProposalService Proposals(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAgentProposalService>();

        /// <summary>Settles the proposal into the named engine lifecycle outcome, leaving the originating turn paused.</summary>
        public async Task SettleAsync(IServiceScope scope, AgentSession session, AgentActionProposal proposal, string outcome)
        {
            var proposals = Proposals(scope);
            if (outcome == "denied") Assert.True((await proposals.DenyAsync(proposal.Id, session.ActorId, "no")).Succeeded);
            else
            {
                Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
                if (outcome == "refused") Authority.Withdrawn.Add(DeterministicAgentProvider.MutatingToolName);
                await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
            }
            AssertLifecycle(Assert.IsType<AgentToolResult>(await proposals.FindResultAsync(proposal.Id)), outcome, proposal);
        }

        public async Task<(AgentSession Session, AgentActionProposal Proposal)> ProposeAsync(IServiceScope scope)
        {
            var session = await Session(Sessions(scope));
            await Sessions(scope).AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null, [], []));
            var first = await Collect(scope, session.Id);
            return (session, Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload));
        }
    }

    /// <summary>Native host authority: read is required for every tool and for owner disclosure; write is withdrawable per tool.</summary>
    private sealed class NativeAuthority : IAgentToolAuthorizer
    {
        public bool Read { get; set; } = true;
        public HashSet<string> Withdrawn { get; } = new(StringComparer.Ordinal);
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
        {
            var allowed = Read && !Withdrawn.Contains(tool.Name);
            return Task.FromResult(new AgentPolicyDecision(allowed, allowed ? [] : [new("test.permission_denied", "Current native authority denied.")]));
        }
    }

    /// <summary>The module's disclosure owner: it admits its own retained evidence only under current read authority.</summary>
    private sealed class GatedSource(NativeAuthority authority) : IAgentContextProvider
    {
        public const string Id = "gated.source";
        public const string UnavailableCode = "gated.authority.unavailable";
        public int ResultChecks { get; private set; }
        /// <summary>The owner cannot currently decide (an outage), as distinct from refusing.</summary>
        public bool Unavailable { get; set; }
        public AgentContextProviderDescriptor Descriptor { get; } = new(Id, GatedTool.Module, Id, [], ["1"], [GatedTool.Target.ResourceType]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            ResultChecks += request.Results.Count;
            if (Unavailable)
                return ValueTask.FromResult(new AgentContextContribution([],
                    [new(Id, UnavailableCode, "Owner store is down: private detail.", true) { OwnerUnavailable = true }]));
            return ValueTask.FromResult(authority.Read
                ? new AgentContextContribution(request.Context, [])
                : new([], [new(Id, "gated.read_denied", "Current read authority is required.", true)]));
        }
    }

    private sealed class GatedTool(bool receipt) : IAgentTool
    {
        public const string Module = "gated.module";
        public const string ResultType = "gated.change";
        public static readonly AgentResourceTarget Target = new(Module, "gated.resource", "resource-1", "revision-1");
        public int Executions { get; private set; }
        public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.MutatingToolName, "Resize", "Changes the owned resource.",
            AgentToolMutability.Mutating, AgentRisk.ReviewRequired, "{}", [], [])
        { ModuleId = Module, DisclosureSourceId = GatedSource.Id, ResultType = ResultType, ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with
            { Target = Target, RequiresApproval = true }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            var data = new AgentPayload(ResultType, "1", JsonSerializer.SerializeToElement(new { size = "medium" }));
            var result = new AgentToolResult(invocation.ToolCallId, Descriptor.Name, true, "applied", data);
            return Task.FromResult(AgentResult<AgentToolResult>.Success(receipt
                ? result with { Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = "revision-2" }, AgentEffectScope.ServerPersisted, data) }
                : result));
        }
    }

    /// <summary>A module read whose own failure result imitates the engine's lifecycle envelope for its stamped invocation.</summary>
    private sealed class ForgingReadTool : IAgentTool
    {
        public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.ReadOnlyToolName, "Read", "Reads the owned resource.",
            AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, "{}", [], [])
        { ModuleId = GatedTool.Module, DisclosureSourceId = GatedSource.Id, ResultType = "gated.read", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = GatedTool.Target }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, false, "refused",
                new("agent.operation-outcome", "1", JsonSerializer.SerializeToElement(new { status = "refused", invocation.InvocationId })))));
    }

    private sealed class CapturingProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider _inner = new();
        public string ProviderId => _inner.ProviderId;
        public List<AgentTurnContext> Inputs { get; } = [];
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken)) yield return item;
        }
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
    }
}
