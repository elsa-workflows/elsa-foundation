using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class AgentDisclosureBoundaryTests
{
    private const string ModuleId = "boundary.module";
    private const string SourceId = "boundary.source";
    private const string ToolName = "boundary_change";
    private const string PrivateOutcome = "MODULE_OUTCOME_REQUIRES_CURRENT_READ_AUTHORITY";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_driven_result_is_withheld_after_the_effect_when_read_authority_or_host_policy_changes(bool hostPolicy)
    {
        using var fixture = new Fixture();
        fixture.Provider.EmitPrefix = true;
        fixture.Tool.AfterEffect = () => { if (hostPolicy) fixture.Policy.DenyTool = true; else fixture.Source.Allowed = false; };
        var session = await fixture.CreateSession();
        await fixture.Post(session, true);

        var events = await fixture.Stream(session.Id);

        Assert.Equal(1, fixture.Tool.Executions);
        Assert.NotNull(fixture.Tool.Receipt);
        Assert.Single(fixture.Provider.Inputs);
        Assert.Contains(events, item => item.Kind is AgentStreamEventKind.Error or AgentStreamEventKind.TurnCancelled);
        Assert.DoesNotContain(fixture.Provider.Inputs, input => JsonSerializer.Serialize(input).Contains(PrivateOutcome, StringComparison.Ordinal));
        await fixture.AssertRetainedReceipt(session.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_Copilot_callback_does_not_return_the_newly_forbidden_result_to_the_SDK(bool hostPolicy)
    {
        using var fixture = new Fixture(copilot: true);
        fixture.Tool.AfterEffect = () => { if (hostPolicy) fixture.Policy.DenyTool = true; else fixture.Source.Allowed = false; };
        var session = await fixture.CreateSession();
        await fixture.Post(session, true);

        var events = await fixture.Stream(session.Id);

        Assert.Equal(1, fixture.Tool.Executions);
        Assert.True(fixture.Sdk.CallbackAttempted);
        Assert.True(fixture.Sdk.CancellationObserved || fixture.Sdk.Aborted);
        Assert.DoesNotContain(PrivateOutcome, fixture.Sdk.Returned ?? string.Empty);
        Assert.DoesNotContain(PrivateOutcome, fixture.Sdk.CallbackError?.ToString() ?? string.Empty);
        Assert.Contains(events, item => item.Kind is AgentStreamEventKind.Error or AgentStreamEventKind.TurnCancelled);
        await fixture.AssertRetainedReceipt(session.Id);
    }

    [Theory]
    [InlineData("tool-only")]
    [InlineData("error")]
    [InlineData("cancelled")]
    public async Task Tool_evidence_without_assistant_text_survives_the_terminal_path_and_protects_later_history(string terminal)
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture();
        fixture.Provider.Terminal = terminal;
        if (terminal == "cancelled") fixture.Provider.BeforeResultResponse = cancellation.Cancel;
        var session = await fixture.CreateSession();
        await fixture.Post(session, false);

        var first = await fixture.Stream(session.Id, cancellation.Token);

        Assert.Equal(1, fixture.Tool.Executions);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.MessageDelta);
        await fixture.AssertRetainedReceipt(session.Id);
        var inputsBefore = fixture.Provider.Inputs.Count;
        fixture.Source.Allowed = false;
        fixture.Provider.BeforeResultResponse = null;
        await fixture.Post(session, false);

        var denied = await fixture.Stream(session.Id);

        Assert.Equal(inputsBefore, fixture.Provider.Inputs.Count);
        Assert.Contains(denied, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Contains(fixture.Source.Disclosures, request => request.IsHistorical && request.Results.Count > 0);
        Assert.Equal(1, fixture.Tool.Executions);
    }

    [Fact]
    public async Task Actual_Copilot_tool_only_outcome_is_journaled_and_new_authority_blocks_SDK_session_reuse()
    {
        using var fixture = new Fixture(copilot: true, includeContext: false);
        var session = await fixture.CreateSession();
        await fixture.Post(session, false);

        var first = await fixture.Stream(session.Id);

        Assert.Contains(first, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains(PrivateOutcome, fixture.Sdk.Returned ?? string.Empty);
        await fixture.AssertRetainedReceipt(session.Id);
        var sdkTurns = fixture.Sdk.Turns;
        fixture.Source.Allowed = false;
        await fixture.Post(session, false);

        var denied = await fixture.Stream(session.Id);

        Assert.Equal(sdkTurns, fixture.Sdk.Turns);
        Assert.Contains(denied, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Contains(fixture.Source.Disclosures, request => request.IsHistorical && request.Results.Count > 0);
        Assert.Equal(1, fixture.Tool.Executions);
    }

    [Fact]
    public async Task A_result_without_context_is_admitted_by_its_exact_source_instead_of_skipping_owner_authority()
    {
        var decoy = new Source("same-module-unrelated-source", false);
        using var fixture = new Fixture(extraSource: decoy, includeContext: false);
        var session = await fixture.CreateSession();
        var result = await fixture.Dispatch(session, []);
        fixture.Source.Allowed = false;
        fixture.Source.Disclosures.Clear();

        var admitted = await fixture.Collector.CollectAsync(session.Policy, new(session.Id, string.Empty, new Dictionary<string, string>())
        { Disclosure = new(session.Id, [], [result], true) });

        Assert.False(admitted.Succeeded);
        var disclosure = Assert.Single(fixture.Source.Disclosures);
        Assert.Empty(disclosure.Context);
        Assert.Equal(result, Assert.Single(disclosure.Results));
        Assert.Empty(decoy.Disclosures);
        Assert.Equal(SourceId, result.Access?.DisclosureSourceId);
        Assert.False(string.IsNullOrWhiteSpace(result.Access?.InvocationId));
        Assert.Equal(1, fixture.Tool.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_projection_to_public_guidance_cannot_bypass_policy_on_the_original_resource(bool sensitivity)
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateSession();
        var original = await fixture.Context(session.Id);
        var result = await fixture.Dispatch(session, original);
        fixture.Source.GuidanceOnly = true;
        var restricted = sensitivity
            ? session.Policy with { MaxContextSensitivity = AgentContextSensitivity.Public }
            : session.Policy with { AllowedContextKinds = ["boundary.guidance"] };

        var admitted = await fixture.Collector.CollectAsync(restricted, new(session.Id, string.Empty, new Dictionary<string, string>())
        { Disclosure = new(session.Id, original, [result], true) });

        Assert.False(admitted.Succeeded);
        Assert.Equal(1, fixture.Tool.Executions);
        Assert.NotNull(fixture.Tool.Receipt);
        Assert.Equal("revision-2", fixture.Tool.Receipt.After.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_authority_during_admission_releases_a_new_prompt_or_preserves_the_accepted_receipt_pause(bool receiptPause)
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateSession(review: receiptPause);
        AgentPausedTurn? pause = null;
        AgentToolResult? accepted = null;
        if (receiptPause)
        {
            await fixture.Post(session, true);
            var first = await fixture.Stream(session.Id);
            var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
            Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            Assert.True((await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            accepted = await fixture.Proposals.FindResultAsync(proposal.Id);
            Assert.NotNull(accepted?.Receipt);
            pause = fixture.Pauses.Find(session.Id);
            Assert.NotNull(pause);
        }
        else
        {
            accepted = await fixture.Dispatch(session, []);
            await fixture.Sessions.AddMessageAsync(session.Id, new(AgentRole.Tool, "Retained owner outcome", AgentMessageStatus.Completed, null, [], []) { ToolResults = [accepted] });
            await fixture.Post(session, false);
        }
        fixture.Authority.Throw = true;
        var error = await Record.ExceptionAsync(async () => await fixture.Stream(session.Id));
        var domain = Assert.IsType<AgentContributionException>(error);
        Assert.IsType<InvalidOperationException>(domain.InnerException);
        Assert.Contains(session.Id, domain.Message, StringComparison.Ordinal);
        Assert.False(fixture.Turns.IsActive(session.Id));

        if (receiptPause)
        {
            Assert.Equal(pause, fixture.Pauses.Find(session.Id));
            Assert.Equal(accepted, await fixture.Proposals.FindResultAsync(pause!.ProposalId));
            Assert.Equal(1, fixture.Tool.Executions);
        }
        else
        {
            var prompt = Assert.Single(await fixture.Sessions.ListMessagesAsync(session.Id), message => message.Role == AgentRole.User);
            Assert.DoesNotContain(prompt.Status, new[] { AgentMessageStatus.Pending, AgentMessageStatus.Streaming });
            await fixture.Post(session, false);
            await fixture.AssertRetainedReceipt(session.Id);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        private readonly bool copilot;
        internal Source Source { get; }
        internal Authority Authority { get; } = new();
        internal PolicyGate Policy { get; } = new();
        internal EffectTool Tool { get; } = new();
        internal ScriptedProvider Provider { get; } = new();
        internal SdkSession Sdk { get; } = new();
        internal IAgentSessionService Sessions => scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        internal IAgentContextCollector Collector => scope.ServiceProvider.GetRequiredService<IAgentContextCollector>();
        internal IAgentProposalService Proposals => scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        internal IAgentTurnStateStore Pauses => scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>();
        internal IAgentTurnRegistry Turns => scope.ServiceProvider.GetRequiredService<IAgentTurnRegistry>();
        internal Fixture(bool copilot = false, Source? extraSource = null, bool includeContext = true)
        {
            this.copilot = copilot;
            Source = new(SourceId, includeContext);
            root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentTool>(Tool);
                services.AddScoped<IAgentContextProvider>(_ => Source);
                if (extraSource is not null) services.AddScoped<IAgentContextProvider>(_ => extraSource);
                services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => Authority));
                services.Replace(ServiceDescriptor.Scoped<IAgentPolicyEvaluator>(_ => Policy));
                if (copilot)
                    services.AddScoped<IAgentProvider>(provider => new GitHubCopilotAgentProvider(new SdkFactory(Sdk),
                        Options.Create(new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = Guid.NewGuid().ToString("N") }),
                        provider.GetRequiredService<IAgentToolInvoker>(), NullLogger<GitHubCopilotAgentProvider>.Instance));
                else services.AddSingleton<IAgentProvider>(Provider);
            });
            scope = root.CreateScope();
        }
        internal Task<AgentSession> CreateSession(bool review = false)
            => Sessions.CreateAsync(new("tenant", "actor", "conversation", copilot ? "github-copilot" : Provider.ProviderId, "explain", null,
                review ? AgentPolicy.Default : AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto }, new Dictionary<string, string>()));
        internal async Task<IReadOnlyCollection<AgentContextAttachment>> Context(string sessionId)
        {
            var context = await Collector.CollectAsync(AgentPolicy.Default, new(sessionId, "*", new Dictionary<string, string>()));
            Assert.True(context.Succeeded);
            return context.Value!.Attachments;
        }
        internal async Task Post(AgentSession session, bool context)
        {
            IReadOnlyCollection<AgentContextAttachment> attachments = context ? await Context(session.Id) : [];
            await Sessions.AddMessageAsync(session.Id, new(AgentRole.User, "Apply the declared resource operation", AgentMessageStatus.Pending, null,
                attachments.Select(item => item.Id).ToArray(), attachments));
        }
        internal async Task<List<AgentStreamEvent>> Stream(string sessionId, CancellationToken token = default)
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
            try { return await Read(sessionId, bound.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { bound.Cancel(); }
        }
        private async Task<List<AgentStreamEvent>> Read(string sessionId, CancellationToken token)
        {
            var events = new List<AgentStreamEvent>();
            await foreach (var item in scope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId, token)) events.Add(item);
            return events;
        }
        internal async Task<AgentToolResult> Dispatch(AgentSession session, IReadOnlyCollection<AgentContextAttachment> context)
        {
            var invocation = new AgentToolInvocation("direct-call", ToolName, session.Id, session.ActorId, new Dictionary<string, object?>(), context)
            { TurnId = "direct-turn", TenantId = session.TenantId };
            var dispatch = await scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>().DispatchAsync(invocation, session.Policy);
            Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
            return Assert.IsType<AgentToolResult>(dispatch.Result);
        }
        internal async Task AssertRetainedReceipt(string sessionId)
        {
            var results = (await Sessions.ListMessagesAsync(sessionId)).SelectMany(message => message.ToolResults).ToArray();
            var retained = Assert.Single(results.Where(result => result.Receipt?.InvocationId == Tool.Receipt?.InvocationId)
                .DistinctBy(result => result.Access?.InvocationId));
            Assert.Equal(Tool.Receipt, retained.Receipt);
            Assert.Equal(SourceId, retained.Access?.DisclosureSourceId);
            Assert.Equal(retained.Receipt!.InvocationId, retained.Access?.InvocationId);
            Assert.Equal(1, Tool.Executions);
        }
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class Source(string id, bool includeContext) : IAgentContextProvider
    {
        internal bool Allowed = true;
        internal bool GuidanceOnly;
        internal List<AgentContextDisclosureRequest> Disclosures { get; } = [];
        public AgentContextProviderDescriptor Descriptor { get; } = new(id, ModuleId, "*", ["boundary.resource", "boundary.guidance"], ["1"], ["boundary-resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution(includeContext ? [Resource()] : [], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            Disclosures.Add(request);
            if (!Allowed || request.Results.Any(result => result.Access?.DisclosureSourceId != id))
                return ValueTask.FromResult(new AgentContextContribution([], [new(id, "boundary.read_denied", "Current resource read authority is required.", true)]));
            return ValueTask.FromResult(new AgentContextContribution(GuidanceOnly
                ? [new("guidance", ModuleId, null, "Installed guidance", "boundary.guidance", AgentContextSensitivity.Public, "guidance", "Public installed guidance", null, new Dictionary<string, string>())
                    { ModuleId = ModuleId, Origin = AgentContextOrigin.InstalledGuidance }]
                : request.Context, []));
        }
        private static AgentContextAttachment Resource() => new("resource", ModuleId, "one", "Owner resource", "boundary.resource", AgentContextSensitivity.Internal,
            "resource", "Bounded owner evidence", new { resourceValue = "original-value" }, new Dictionary<string, string>())
        { ModuleId = ModuleId, Target = new(ModuleId, "boundary-resource", "one", "revision-1") };
    }

    private sealed class Authority : IAgentToolAuthorizer
    {
        internal bool Throw;
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
            => Throw ? throw new InvalidOperationException("Fixture authority is temporarily unavailable.") : Task.FromResult(new AgentPolicyDecision(true, []));
    }

    private sealed class PolicyGate : IAgentPolicyEvaluator
    {
        private readonly DefaultAgentPolicyEvaluator inner = new();
        internal bool DenyTool;
        public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default) => inner.EvaluateAvailabilityAsync(policy, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.EvaluateContextAsync(policy, attachments, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
            => inner.EvaluateCapabilityAsync(DenyTool ? policy with { DeniedCapabilityIds = [ToolName] } : policy, capabilityId, cancellationToken);
    }

    private sealed class EffectTool : IAgentTool
    {
        internal int Executions;
        internal AgentEffectReceipt? Receipt;
        internal Action? AfterEffect;
        public AgentToolDescriptor Descriptor { get; } = new(ToolName, "Declared boundary change", "Apply one owner-observed change", AgentToolMutability.Mutating,
            AgentRisk.ReviewRequired, "{\"type\":\"object\"}", [], [])
        { ModuleId = ModuleId, DisclosureSourceId = SourceId, ResultType = "boundary.result", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with
            { Target = new(ModuleId, "boundary-resource", "one", "revision-1") }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            var payload = new AgentPayload("boundary.result", "1", JsonSerializer.SerializeToElement(new { observed = PrivateOutcome, executions = Executions }));
            Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = "revision-2" }, AgentEffectScope.ServerPersisted, payload);
            AfterEffect?.Invoke();
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, ToolName, true, PrivateOutcome, payload) { Receipt = Receipt }));
        }
    }

    private sealed class ScriptedProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider inner = new();
        internal List<AgentTurnContext> Inputs { get; } = [];
        internal bool EmitPrefix;
        internal string Terminal = "tool-only";
        internal Action? BeforeResultResponse;
        public string ProviderId => inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.CompletedTask;
            if (Inputs.Count == 1)
            {
                if (EmitPrefix) yield return Event(AgentStreamEventKind.MessageDelta, content: "Inspecting the declared operation.");
                yield return Event(AgentStreamEventKind.ToolCallRequested, new AgentToolCallRequest("provider-call", ToolName, "{}", AgentRisk.ReviewRequired, true));
            }
            else
            {
                BeforeResultResponse?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (Terminal == "error") yield return Event(AgentStreamEventKind.Error, error: new("fixture.provider_failed", "Provider ended without assistant text."));
            }
            yield return Event(AgentStreamEventKind.Completed);
        }
    }

    private sealed class SdkFactory(SdkSession session) : IGitHubCopilotClientFactory
    { public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new SdkClient(session); }
    private sealed class SdkClient(SdkSession session) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) { session.Tools = request.CustomTools ?? []; return Task.FromResult<IGitHubCopilotSession>(session); }
        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) => CreateSessionAsync(request, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class SdkSession : IGitHubCopilotSession
    {
        internal IReadOnlyCollection<AIFunction> Tools = [];
        internal int Turns;
        internal bool CallbackAttempted, CancellationObserved, Aborted;
        internal string? Returned;
        internal Exception? CallbackError;
        public string SessionId => "sdk-boundary-session";
        public string? WorkspacePath => null;
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Turns++;
            using var registration = cancellationToken.Register(() => CancellationObserved = true);
            yield return new(GitHubCopilotStreamEventKind.Started);
            var tool = Assert.Single(Tools);
            CallbackAttempted = true;
            try { Returned = JsonSerializer.Serialize(await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken)); }
            catch (Exception error) { CallbackError = error; }
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }
        public Task AbortAsync(CancellationToken cancellationToken) { Aborted = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AgentStreamEvent Event(AgentStreamEventKind kind, object? payload = null, string? content = null, AgentError? error = null)
        => new(Guid.NewGuid().ToString("N"), kind, content, null, error, DateTimeOffset.UtcNow, Payload: payload);
}
