using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Weaver.GitHubCopilot.Services;
using Elsa.Weaver.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Weaver.Tests;

public sealed class WeaverDisclosureBoundaryTests
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
        Assert.Contains(events, item => item.Kind is WeaverStreamEventKind.Error or WeaverStreamEventKind.TurnCancelled);
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
        Assert.Contains(events, item => item.Kind is WeaverStreamEventKind.Error or WeaverStreamEventKind.TurnCancelled);
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
        Assert.DoesNotContain(first, item => item.Kind == WeaverStreamEventKind.MessageDelta);
        await fixture.AssertRetainedReceipt(session.Id);
        var inputsBefore = fixture.Provider.Inputs.Count;
        fixture.Source.Allowed = false;
        fixture.Provider.BeforeResultResponse = null;
        await fixture.Post(session, false);

        var denied = await fixture.Stream(session.Id);

        Assert.Equal(inputsBefore, fixture.Provider.Inputs.Count);
        Assert.Contains(denied, item => item.Kind == WeaverStreamEventKind.Error);
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

        Assert.Contains(first, item => item.Kind == WeaverStreamEventKind.Completed);
        Assert.DoesNotContain(first, item => item.Kind == WeaverStreamEventKind.MessageDelta);
        Assert.Contains(PrivateOutcome, fixture.Sdk.Returned ?? string.Empty);
        await fixture.AssertRetainedReceipt(session.Id);
        var sdkTurns = fixture.Sdk.Turns;
        fixture.Source.Allowed = false;
        await fixture.Post(session, false);

        var denied = await fixture.Stream(session.Id);

        Assert.Equal(sdkTurns, fixture.Sdk.Turns);
        Assert.Contains(denied, item => item.Kind == WeaverStreamEventKind.Error);
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
            ? session.Policy with { MaxContextSensitivity = WeaverContextSensitivity.Public }
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
        WeaverPausedTurn? pause = null;
        WeaverToolResult? accepted = null;
        if (receiptPause)
        {
            await fixture.Post(session, true);
            var first = await fixture.Stream(session.Id);
            var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(first, item => item.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
            Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            Assert.True((await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            accepted = await fixture.Proposals.FindResultAsync(proposal.Id);
            Assert.NotNull(accepted?.Receipt);
            pause = await fixture.Pauses.FindAsync(session.Id);
            Assert.NotNull(pause);
        }
        else
        {
            accepted = await fixture.Dispatch(session, []);
            await fixture.Sessions.AddMessageAsync(session.Id, new(WeaverRole.Tool, "Retained owner outcome", WeaverMessageStatus.Completed, null, [], []) { ToolResults = [accepted] });
            await fixture.Post(session, false);
        }
        fixture.Authority.Throw = true;
        var error = await Record.ExceptionAsync(async () => await fixture.Stream(session.Id));
        var domain = Assert.IsType<WeaverContributionException>(error);
        Assert.IsType<InvalidOperationException>(domain.InnerException);
        Assert.Contains(session.Id, domain.Message, StringComparison.Ordinal);
        Assert.False(fixture.Turns.IsActive(session.Id));

        if (receiptPause)
        {
            Assert.Equal(pause, await fixture.Pauses.FindAsync(session.Id));
            Assert.Equal(accepted, await fixture.Proposals.FindResultAsync(pause!.ProposalId));
            Assert.Equal(1, fixture.Tool.Executions);
        }
        else
        {
            var prompt = Assert.Single(await fixture.Sessions.ListMessagesAsync(session.Id), message => message.Role == WeaverRole.User);
            Assert.DoesNotContain(prompt.Status, new[] { WeaverMessageStatus.Pending, WeaverMessageStatus.Streaming });
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
        internal IWeaverSessionService Sessions => scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        internal IWeaverContextCollector Collector => scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>();
        internal IWeaverProposalService Proposals => scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
        internal IWeaverTurnStateStore Pauses => scope.ServiceProvider.GetRequiredService<IWeaverTurnStateStore>();
        internal IWeaverTurnRegistry Turns => scope.ServiceProvider.GetRequiredService<IWeaverTurnRegistry>();
        internal Fixture(bool copilot = false, Source? extraSource = null, bool includeContext = true)
        {
            this.copilot = copilot;
            Source = new(SourceId, includeContext);
            root = WeaverTestServices.Create(services =>
            {
                services.AddSingleton<IWeaverTool>(Tool);
                services.AddScoped<IWeaverContextProvider>(_ => Source);
                if (extraSource is not null) services.AddScoped<IWeaverContextProvider>(_ => extraSource);
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => Authority));
                services.Replace(ServiceDescriptor.Scoped<IWeaverPolicyEvaluator>(_ => Policy));
                if (copilot)
                    services.AddScoped<IWeaverProvider>(provider => new WeaverGitHubCopilotProvider(new SdkFactory(Sdk),
                        Options.Create(new WeaverGitHubCopilotOptions { Enabled = true, GitHubToken = Guid.NewGuid().ToString("N") }),
                        provider.GetRequiredService<IWeaverToolInvoker>(), NullLogger<WeaverGitHubCopilotProvider>.Instance));
                else services.AddSingleton<IWeaverProvider>(Provider);
            });
            scope = root.CreateScope();
        }
        internal Task<WeaverSession> CreateSession(bool review = false)
            => Sessions.CreateAsync(new("tenant", "actor", "conversation", copilot ? "github-copilot" : Provider.ProviderId, "explain", null,
                review ? WeaverPolicy.Default : WeaverPolicy.Default with { AutonomyMode = WeaverAutonomyMode.FullAuto }, new Dictionary<string, string>()));
        internal async Task<IReadOnlyCollection<WeaverContextAttachment>> Context(string sessionId)
        {
            var context = await Collector.CollectAsync(WeaverPolicy.Default, new(sessionId, "*", new Dictionary<string, string>()));
            Assert.True(context.Succeeded);
            return context.Value!.Attachments;
        }
        internal async Task Post(WeaverSession session, bool context)
        {
            IReadOnlyCollection<WeaverContextAttachment> attachments = context ? await Context(session.Id) : [];
            await Sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "Apply the declared resource operation", WeaverMessageStatus.Pending, null,
                attachments.Select(item => item.Id).ToArray(), attachments));
        }
        internal async Task<List<WeaverStreamEvent>> Stream(string sessionId, CancellationToken token = default)
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
            try { return await Read(sessionId, bound.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { bound.Cancel(); }
        }
        private async Task<List<WeaverStreamEvent>> Read(string sessionId, CancellationToken token)
        {
            var events = new List<WeaverStreamEvent>();
            await foreach (var item in scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId, token)) events.Add(item);
            return events;
        }
        internal async Task<WeaverToolResult> Dispatch(WeaverSession session, IReadOnlyCollection<WeaverContextAttachment> context)
        {
            var invocation = new WeaverToolInvocation("direct-call", ToolName, session.Id, session.ActorId, new Dictionary<string, object?>(), context)
            { TurnId = "direct-turn", TenantId = session.TenantId };
            var dispatch = await scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>().DispatchAsync(invocation, session.Policy);
            Assert.Equal(WeaverToolDispatchOutcome.Executed, dispatch.Outcome);
            return Assert.IsType<WeaverToolResult>(dispatch.Result);
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

    private sealed class Source(string id, bool includeContext) : IWeaverContextProvider
    {
        internal bool Allowed = true;
        internal bool GuidanceOnly;
        internal List<WeaverContextDisclosureRequest> Disclosures { get; } = [];
        public WeaverContextProviderDescriptor Descriptor { get; } = new(id, ModuleId, "*", ["boundary.resource", "boundary.guidance"], ["1"], ["boundary-resource"]);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution(includeContext ? [Resource()] : [], []));
        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            Disclosures.Add(request);
            if (!Allowed || request.Results.Any(result => result.Access?.DisclosureSourceId != id))
                return ValueTask.FromResult(new WeaverContextContribution([], [new(id, "boundary.read_denied", "Current resource read authority is required.", true)]));
            return ValueTask.FromResult(new WeaverContextContribution(GuidanceOnly
                ? [new("guidance", ModuleId, null, "Installed guidance", "boundary.guidance", WeaverContextSensitivity.Public, "guidance", "Public installed guidance", null, new Dictionary<string, string>())
                    { ModuleId = ModuleId, Origin = WeaverContextOrigin.InstalledGuidance }]
                : request.Context, []));
        }
        private static WeaverContextAttachment Resource() => new("resource", ModuleId, "one", "Owner resource", "boundary.resource", WeaverContextSensitivity.Internal,
            "resource", "Bounded owner evidence", new { resourceValue = "original-value" }, new Dictionary<string, string>())
        { ModuleId = ModuleId, Target = new(ModuleId, "boundary-resource", "one", "revision-1") };
    }

    private sealed class Authority : IWeaverToolAuthorizer
    {
        internal bool Throw;
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
            => Throw ? throw new InvalidOperationException("Fixture authority is temporarily unavailable.") : Task.FromResult(new WeaverPolicyDecision(true, []));
    }

    private sealed class PolicyGate : IWeaverPolicyEvaluator
    {
        private readonly DefaultWeaverPolicyEvaluator inner = new();
        internal bool DenyTool;
        public ValueTask<WeaverPolicyDecision> EvaluateAvailabilityAsync(WeaverPolicy policy, CancellationToken cancellationToken = default) => inner.EvaluateAvailabilityAsync(policy, cancellationToken);
        public ValueTask<WeaverPolicyDecision> EvaluateContextAsync(WeaverPolicy policy, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.EvaluateContextAsync(policy, attachments, cancellationToken);
        public ValueTask<WeaverPolicyDecision> EvaluateCapabilityAsync(WeaverPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
            => inner.EvaluateCapabilityAsync(DenyTool ? policy with { DeniedCapabilityIds = [ToolName] } : policy, capabilityId, cancellationToken);
    }

    private sealed class EffectTool : IWeaverTool
    {
        internal int Executions;
        internal WeaverEffectReceipt? Receipt;
        internal Action? AfterEffect;
        public WeaverToolDescriptor Descriptor { get; } = new(ToolName, "Declared boundary change", "Apply one owner-observed change", WeaverToolMutability.Mutating,
            WeaverRisk.ReviewRequired, "{\"type\":\"object\"}", [], [])
        { ModuleId = ModuleId, DisclosureSourceId = SourceId, ResultType = "boundary.result", ResultSchemaVersion = "1" };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor) with
            { Target = new(ModuleId, "boundary-resource", "one", "revision-1") }));
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            var payload = new WeaverPayload("boundary.result", "1", JsonSerializer.SerializeToElement(new { observed = PrivateOutcome, executions = Executions }));
            Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = "revision-2" }, WeaverEffectScope.ServerPersisted, payload);
            AfterEffect?.Invoke();
            return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, ToolName, true, PrivateOutcome, payload) { Receipt = Receipt }));
        }
    }

    private sealed class ScriptedProvider : IWeaverProvider
    {
        private readonly DeterministicWeaverProvider inner = new();
        internal List<WeaverTurnContext> Inputs { get; } = [];
        internal bool EmitPrefix;
        internal string Terminal = "tool-only";
        internal Action? BeforeResultResponse;
        public string ProviderId => inner.ProviderId;
        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.CompletedTask;
            if (Inputs.Count == 1)
            {
                if (EmitPrefix) yield return Event(WeaverStreamEventKind.MessageDelta, content: "Inspecting the declared operation.");
                yield return Event(WeaverStreamEventKind.ToolCallRequested, new WeaverToolCallRequest("provider-call", ToolName, "{}", WeaverRisk.ReviewRequired, true));
            }
            else
            {
                BeforeResultResponse?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (Terminal == "error") yield return Event(WeaverStreamEventKind.Error, error: new("fixture.provider_failed", "Provider ended without assistant text."));
            }
            yield return Event(WeaverStreamEventKind.Completed);
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

    private static WeaverStreamEvent Event(WeaverStreamEventKind kind, object? payload = null, string? content = null, WeaverError? error = null)
        => new(Guid.NewGuid().ToString("N"), kind, content, null, error, DateTimeOffset.UtcNow, Payload: payload);
}
