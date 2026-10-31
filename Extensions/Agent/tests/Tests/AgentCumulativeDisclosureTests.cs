using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentCumulativeDisclosureTests
{
    private const string Module = "cumulative.module";
    private const string SourceId = "cumulative.context";
    private const string DerivedText = "The answer used the authorized detail resource at revision one.";

    [Theory]
    [InlineData(false, "withdrawn")]
    [InlineData(true, "withdrawn")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "restored")]
    [InlineData(true, "restored")]
    public async Task A_context_only_answer_retains_its_added_resource_dependency(bool delegated, string authority)
    {
        using var fixture = new Fixture(delegated, Script.Answer);
        await fixture.Post("First request");
        var first = await fixture.Stream();
        Assert.Contains(first, item => item.Content == DerivedText);
        Assert.Single(fixture.Probe.Inputs);
        Assert.Contains(fixture.Probe.Inputs[0].Context, item => item.Target?.ResourceId == "detail-resource");
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);

        fixture.Source.ShowDetail = false;
        fixture.Source.SetAuthority(authority);
        await fixture.Post("A later request with only the original resource");
        var second = await fixture.Stream();

        if (authority == "withdrawn")
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(second);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.Contains(fixture.Probe.Inputs[1].History, item => item.Content.Contains(DerivedText, StringComparison.Ordinal));
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Omitting_a_disclosed_resource_does_not_authorize_a_later_callback_or_step(bool delegated, bool withdraw)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer);
        fixture.Probe.BeforeOperation = () =>
        {
            fixture.Source.ShowDetail = false;
            if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        };
        await fixture.Post("Read after discussing the detail");

        var events = await fixture.Stream();

        Assert.Contains(events, item => item.Content == DerivedText);
        if (withdraw)
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.False(fixture.Probe.AfterCallbackEntered);
            Assert.Equal(0, fixture.Read.Preparations);
            Assert.Equal(0, fixture.Read.Executions);
            AssertDisclosureDenied(events);
        }
        else
        {
            Assert.Equal(1, fixture.Read.Executions);
            Assert.True(delegated ? fixture.Probe.AfterCallbackEntered : fixture.Probe.Inputs.Count == 2);
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, "withdrawn")]
    [InlineData(true, "withdrawn")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "restored")]
    [InlineData(true, "restored")]
    public async Task Pause_preserves_earlier_disclosure_after_the_current_projection_omits_it(bool delegated, string authority)
    {
        using var fixture = new Fixture(delegated, Script.PauseThenAnswer);
        fixture.Probe.BeforeOperation = () => fixture.Source.ShowDetail = false;
        await fixture.Post("Make one reviewed change after discussing the detail");
        var first = await fixture.Stream();
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first,
            item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.Contains(first, item => item.Content == DerivedText);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
        var applied = await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision);
        var receipt = Assert.IsType<AgentEffectReceipt>(applied.Value?.ToolResult?.Receipt);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal("changed", fixture.Mutation.Value);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, receipt.InvocationId);
        Assert.Equal(proposal.PreparedInvocation.Target, receipt.Before);
        fixture.Source.SetAuthority(authority);

        var resumed = await fixture.Stream();

        if (authority == "withdrawn")
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(resumed);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            var continuation = fixture.Probe.Inputs[1];
            Assert.Empty(continuation.Tools);
            Assert.Equal(receipt, Assert.Single(continuation.Results).Receipt);
            Assert.DoesNotContain(resumed, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(receipt, (await proposals.FindResultAsync(proposal.Id))?.Receipt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_attachment_identity_at_a_new_revision_cannot_erase_the_original_snapshot(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer);
        fixture.Probe.BeforeOperation = () => fixture.Source.DetailRevision = "detail-v2";
        await fixture.Post("Read both revisions without changing the resource");
        var first = await fixture.Stream();
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.Contains(fixture.Probe.Inputs[0].Context, item => item.Id == "detail" && item.Target?.Revision == "detail-v1");
        var journal = await fixture.Services.GetRequiredService<IAgentSessionService>().ListMessagesAsync(fixture.Session!.Id);
        Assert.Contains(journal, message => message.Role == AgentRole.Tool
            && message.Context.Any(item => item.Id == "detail" && item.Target?.Revision == "detail-v2"));
        fixture.Source.ShowDetail = false;
        fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Only the current original resource is requested");
        var entries = fixture.Probe.Inputs.Count;

        var second = await fixture.Stream();

        Assert.Equal(entries, fixture.Probe.Inputs.Count);
        AssertDisclosureDenied(second);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, "completed", true)]
    [InlineData(true, "completed", true)]
    [InlineData(false, "error", true)]
    [InlineData(true, "error", true)]
    [InlineData(false, "cancelled", true)]
    [InlineData(true, "cancelled", true)]
    [InlineData(false, "completed", false)]
    [InlineData(true, "completed", false)]
    [InlineData(false, "error", false)]
    [InlineData(true, "error", false)]
    [InlineData(false, "cancelled", false)]
    [InlineData(true, "cancelled", false)]
    public async Task A_textless_terminal_turn_still_protects_the_provider_transcript(bool delegated, string terminal, bool withdraw)
    {
        using var fixture = new Fixture(delegated, Script.Answer);
        fixture.Probe.TextlessTerminal = terminal;
        fixture.Probe.Cancel = () => fixture.Services.GetRequiredService<IAgentTurnRegistry>().Cancel(fixture.Session!.Id, fixture.Probe.TurnId!);
        await fixture.Post("Receive the detail without producing an answer");
        var first = await fixture.Stream();
        Assert.Single(fixture.Probe.Inputs);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains(first, item => item.Kind == (terminal == "completed" ? AgentStreamEventKind.Completed
            : terminal == "error" ? AgentStreamEventKind.Error : AgentStreamEventKind.TurnCancelled));
        fixture.Probe.TextlessTerminal = null;
        fixture.Source.ShowDetail = false;
        if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Use only the original resource now");

        var second = await fixture.Stream();

        if (withdraw)
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(second);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(fixture.Probe.Inputs[1].History, message => message.Role == AgentRole.Assistant && message.Content.Length == 0);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public async Task A_custom_session_store_cannot_omit_or_change_the_assistant_disclosure_journal(string mutation)
    {
        using var fixture = new Fixture(true, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        await fixture.Post("Produce one answer using the detail");
        Assert.DoesNotContain(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Error);
        fixture.Services.GetRequiredService<SessionBoundary>().Mutation = mutation;
        fixture.Source.ShowDetail = false;
        await fixture.Post("Reuse the retained transcript");

        var second = await fixture.Stream();

        if (mutation == "unchanged")
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(second, item => item.Error?.Code == "agent.history.provenance_unavailable");
        }
    }

    [Fact]
    public async Task A_textless_disclosure_journal_is_retained_without_creating_an_empty_chat_message()
    {
        using var fixture = new Fixture(true, Script.Answer, services => services.AddRouting().AddElsaEndpoints());
        fixture.Probe.TextlessTerminal = "completed";
        await fixture.Post("Read the authorized detail without answering");
        Assert.DoesNotContain(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Error);
        var messages = await fixture.Services.GetRequiredService<IAgentSessionService>().ListMessagesAsync(fixture.Session!.Id);
        var metadata = Assert.Single(messages, message => message.Role == AgentRole.Assistant);
        Assert.Empty(metadata.Content);
        Assert.Contains(metadata.DisclosureHistory!, snapshot => snapshot.Context.Any(item => item.Target?.ResourceId == "detail-resource"));
        var app = new TestEndpointRouteBuilder(fixture.Services);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsGetSession");
        var context = new DefaultHttpContext
        {
            RequestServices = fixture.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "actor-1"), new Claim("elsa.identity.tenant_id", "tenant-1")], "test"))
        };
        context.Request.RouteValues["sessionId"] = fixture.Session.Id;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        var visible = response.RootElement.GetProperty("data").GetProperty("messages").EnumerateArray().ToArray();
        Assert.Single(visible);
        Assert.Equal("user", visible[0].GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(visible[0].GetProperty("content").GetString()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public async Task A_later_pause_storage_change_cannot_admit_a_provider_or_replay_an_accepted_effect(string mutation)
    {
        using var fixture = new Fixture(true, Script.PauseThenAnswer, services =>
        {
            services.AddSingleton<PauseBoundary>();
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
        });
        fixture.Probe.BeforeOperation = () => fixture.Source.ShowDetail = false;
        await fixture.Post("Apply one reviewed change");
        var first = await fixture.Stream();
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
        var applied = await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision);
        var receipt = Assert.IsType<AgentEffectReceipt>(applied.Value?.ToolResult?.Receipt);
        fixture.Services.GetRequiredService<PauseBoundary>().Mutation = mutation;

        var second = await fixture.Stream();

        if (mutation == "unchanged")
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(second, item => item.Error?.Code == "agent.history.provenance_unavailable");
        }
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(receipt, (await proposals.FindResultAsync(proposal.Id))?.Receipt);
    }

    [Fact]
    public void Disclosure_history_snapshots_are_deep_and_preserve_distinct_versions_with_the_same_identity()
    {
        var content = new Dictionary<string, string> { ["value"] = "before" };
        var context = new List<AgentContextAttachment> { HistoryAttachment("same", content) };
        var history = new AgentDisclosureHistory();
        Assert.Null(history.Add(context, []));
        content["value"] = "after";
        Assert.Null(history.Add(context, []));
        context.Clear();
        Assert.Equal(2, history.Snapshots.Count);
        Assert.Equal("before", Assert.IsType<JsonElement>(Assert.Single(history.Snapshots[0].Context).Content).GetProperty("value").GetString());
        Assert.Equal("after", Assert.IsType<JsonElement>(Assert.Single(history.Snapshots[1].Context).Content).GetProperty("value").GetString());
    }

    [Fact]
    public void Count_bound_refuses_an_additional_snapshot_without_discarding_any_prior_dependency()
    {
        var history = new AgentDisclosureHistory();
        for (var index = 0; index < AgentDisclosureHistory.MaximumSnapshots; index++)
            Assert.Null(history.Add([HistoryAttachment("same", new { index })], []));
        var before = AgentInvocationIntegrity.Digest(history.Snapshots);
        Assert.Equal("agent.context.disclosure_budget_exceeded", history.Add([HistoryAttachment("same", new { index = 65 })], [])?.Code);
        Assert.Equal(AgentDisclosureHistory.MaximumSnapshots, history.Snapshots.Count);
        Assert.Equal(before, AgentInvocationIntegrity.Digest(history.Snapshots));
        Assert.Null(history.Add([HistoryAttachment("same", new { index = 0 })], []));
    }

    [Fact]
    public void Byte_bound_refuses_complete_new_evidence_without_evicting_the_retained_history()
    {
        var history = new AgentDisclosureHistory();
        Assert.Null(history.Add([HistoryAttachment("first", new { value = "retained" })], []));
        var before = AgentInvocationIntegrity.Digest(history.Snapshots);
        Assert.Equal("agent.context.disclosure_budget_exceeded", history.Add(
            [HistoryAttachment("too-large", new { value = new string('x', AgentDisclosureHistory.MaximumBytes) })], [])?.Code);
        Assert.Single(history.Snapshots);
        Assert.Equal(before, AgentInvocationIntegrity.Digest(history.Snapshots));
    }

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    public async Task A_new_projection_exceeding_the_retained_bound_is_refused_before_provider_entry(int retained, bool exceeds)
    {
        using var fixture = new Fixture(true, Script.Answer);
        await fixture.Post("An earlier authorized request");
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        var original = Assert.Single(await sessions.ListMessagesAsync(fixture.Session!.Id));
        await sessions.SetMessageStatusAsync(fixture.Session.Id, original.Id, AgentMessageStatus.Completed);
        var attachment = Assert.Single(original.Context);
        var history = AgentDisclosureHistory.Freeze(Enumerable.Range(0, retained).Select(index => new AgentDisclosureSnapshot(
            [index == 0 ? attachment : attachment with { Content = new { snapshot = index } }], [])));
        const string priorAnswer = "The authorized earlier snapshots were observed.";
        await sessions.AddMessageAsync(fixture.Session.Id, new(AgentRole.Assistant, priorAnswer, AgentMessageStatus.Completed, null, [], [])
        {
            HasDisclosureProvenance = true, DisclosureHistory = history,
            DeliveryFingerprint = AgentDisclosureHistory.MessageFingerprint(priorAnswer, [], [], history)
        });
        await fixture.Post("Request the current expanded projection");

        var events = await fixture.Stream();

        if (exceeds)
        {
            Assert.Empty(fixture.Probe.Inputs);
            Assert.Contains(events, item => item.Error?.Code == "agent.context.disclosure_budget_exceeded");
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    private static AgentContextAttachment HistoryAttachment(string id, object content)
        => new(id, Module, "resource", "Resource", "cumulative.resource", AgentContextSensitivity.Internal, "selection", "Evidence", content,
            new Dictionary<string, string>()) { ContributorId = SourceId, ModuleId = Module };

    private static void AssertDisclosureDenied(IEnumerable<AgentStreamEvent> events)
    {
        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error
            && item.Error?.Code == "agent.context.incomplete");
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
    }

    private enum Script { Answer, ReadThenAnswer, PauseThenAnswer }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal ProjectionSource Source { get; } = new();
        internal NeutralReadTool Read { get; } = new();
        internal ProposalMutationTool Mutation { get; } = new();
        internal ProbeState Probe { get; }
        internal AgentSession? Session { get; private set; }
        internal IServiceProvider Services => scope.ServiceProvider;

        internal Fixture(bool delegated, Script script, Action<IServiceCollection>? configure = null)
        {
            Probe = new(script);
            root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentContextProvider>(Source);
                services.AddSingleton<IAgentTool>(Read);
                services.AddSingleton<IAgentTool>(Mutation);
                if (delegated) services.AddSingleton<IAgentProvider>(new DelegatedProbe(Probe));
                else services.AddSingleton<IAgentProvider>(new HostProbe(Probe));
                configure?.Invoke(services);
            });
            scope = root.CreateScope();
        }

        internal async Task Post(string text)
        {
            var sessions = Services.GetRequiredService<IAgentSessionService>();
            Session ??= await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "cumulative-probe", "explain", null,
                AgentPolicy.Default, new Dictionary<string, string>()));
            var collected = await Services.GetRequiredService<IAgentContextCollector>().CollectAsync(Session.Policy,
                new(Session.Id, SourceId, new Dictionary<string, string>()));
            Assert.True(collected.Succeeded, collected.Error?.Message);
            var context = collected.Value!.Attachments;
            await sessions.AddMessageAsync(Session.Id, new(AgentRole.User, text, AgentMessageStatus.Pending, null,
                context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true });
        }

        internal async Task<List<AgentStreamEvent>> Stream()
        {
            var events = new List<AgentStreamEvent>();
            await foreach (var item in Services.GetRequiredService<IAgentStreamingService>().StreamAsync(Session!.Id))
            {
                if (item.Payload is AgentTurnStarted started) Probe.TurnId = started.TurnId;
                events.Add(item);
            }
            return events;
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class ProjectionSource : IAgentContextProvider
    {
        internal bool ShowDetail = true;
        internal string DetailRevision = "detail-v1";
        internal HashSet<string> AllowedDetailRevisions { get; } = ["detail-v1", "detail-v2"];
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["cumulative.resource"], ["1"], ["resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment("original", "original-resource", "original-v1")], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Context.Any(item => item.Target?.ResourceId == "detail-resource" && !AllowedDetailRevisions.Contains(item.Target.Revision!)))
                return ValueTask.FromResult(new AgentContextContribution([], [new(SourceId, "cumulative.read_denied", "Current owner authority withholds this retained detail resource.", true)]));
            if (request.IsHistorical) return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
            return ValueTask.FromResult(new AgentContextContribution(ShowDetail
                ? [Attachment("original", "original-resource", "original-v1"), Attachment("detail", "detail-resource", DetailRevision)]
                : [Attachment("original", "original-resource", "original-v1")], []));
        }
        internal void SetAuthority(string authority)
        {
            if (authority is "withdrawn" or "restored") AllowedDetailRevisions.Remove("detail-v1");
            if (authority == "restored") AllowedDetailRevisions.Add("detail-v1");
        }
        private static AgentContextAttachment Attachment(string id, string resource, string revision)
            => new(id, Module, resource, "Owned resource", "cumulative.resource", AgentContextSensitivity.Internal, "selection",
                resource + " " + revision, new { resource, revision }, new Dictionary<string, string>())
            { ModuleId = Module, Target = new(Module, "resource", resource, revision) };
    }

    private sealed record Input(IReadOnlyCollection<AgentContextAttachment> Context, IReadOnlyList<AgentTurnMessage> History,
        IReadOnlyList<AgentToolResult> Results, IReadOnlyCollection<string> Tools);

    private sealed class ProbeState(Script script)
    {
        internal List<Input> Inputs { get; } = [];
        internal Action? BeforeOperation;
        internal bool Operated, AfterCallbackEntered;
        internal string? TextlessTerminal, TurnId;
        internal Action? Cancel;
        internal bool NeedsOperation => !Operated && script != Script.Answer;
        internal string ToolName => script == Script.PauseThenAnswer ? "test_mutate" : "cumulative_neutral_read";
    }

    private sealed class NeutralReadTool : IAgentTool
    {
        internal int Preparations, Executions;
        public AgentToolDescriptor Descriptor { get; } = new("cumulative_neutral_read", "Read another owned resource", "A neutral owner read.",
            AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, "{}", [], [])
        { ModuleId = "test.module", DisclosureSourceId = AgentTestContextSource.TestModuleSourceId, ResultType = "neutral.result", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(null, JsonSerializer.SerializeToElement(invocation.Arguments),
                AgentRisk.ReadOnly, new("neutral.preview", "1", JsonSerializer.SerializeToElement(new { read = true })), false)));
        }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "The neutral resource was read without changes.",
                new("neutral.result", "1", JsonSerializer.SerializeToElement(new { read = true })))));
        }
    }

    private class HostProbe(ProbeState state) : IAgentProvider
    {
        protected ProbeState State { get; } = state;
        public string ProviderId => "cumulative-probe";
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat, AgentProviderOperation.Streaming], AgentProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            State.Inputs.Add(new(context.Context, context.History, context.PendingToolResults, context.AvailableTools.Select(item => item.Name).ToArray()));
            if (State.TextlessTerminal is { } terminal)
            {
                if (terminal == "cancelled") { State.Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (terminal == "error") yield return new("textless-error", AgentStreamEventKind.Error, null, null, new("probe.error", "The provider stopped without text.", 503), DateTimeOffset.UtcNow);
                yield break;
            }
            yield return Text(DerivedText);
            if (State.NeedsOperation)
            {
                State.Operated = true; State.BeforeOperation?.Invoke();
                yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, null, null, DateTimeOffset.UtcNow,
                    Payload: new AgentToolCallRequest("cumulative-call", State.ToolName, "{\"value\":\"changed\"}", AgentRisk.ReadOnly, false));
            }
        }
        protected static AgentStreamEvent Text(string text) => new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, text, null, null, DateTimeOffset.UtcNow);
    }

    private sealed class DelegatedProbe(ProbeState state) : HostProbe(state), IAgentHarness
    {
        public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools;
        public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            State.Inputs.Add(new(request.Context, request.History, request.PendingToolResults, request.Tools.Select(item => item.Descriptor.Name).ToArray()));
            if (State.TextlessTerminal is { } terminal)
            {
                if (terminal == "cancelled") { State.Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (terminal == "error") yield return new("textless-error", AgentStreamEventKind.Error, null, null, new("probe.error", "The provider stopped without text.", 503), DateTimeOffset.UtcNow);
                yield break;
            }
            yield return Text(DerivedText);
            if (!State.NeedsOperation) yield break;
            State.Operated = true; State.BeforeOperation?.Invoke();
            var dispatch = await request.ToolInvoker!.DispatchAsync(new("cumulative-call", State.ToolName, request.SessionId, request.ActorId,
                new Dictionary<string, object?> { ["value"] = "changed" }, request.Context)
            { TenantId = request.TenantId, TurnId = request.TurnId }, request.Policy, cancellationToken);
            if (dispatch.Proposal is { } proposal)
            {
                yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, Payload: proposal);
                yield break;
            }
            State.AfterCallbackEntered = true;
            yield return Text("The delegated model received the read result after its callback.");
        }
    }

    private static IReadOnlyList<AgentDisclosureSnapshot>? ChangeHistory(IReadOnlyList<AgentDisclosureSnapshot>? history, string? mutation)
        => mutation switch
        {
            "missing" => null,
            "truncated" => [],
            "changed" => history!.Select(snapshot => snapshot with
            { Context = snapshot.Context.Select(item => item with { Summary = "Changed stored provenance" }).ToArray() }).ToArray(),
            _ => history
        };

    private sealed class PauseBoundary : IAgentTurnStateStore
    {
        private AgentPausedTurn? state;
        internal string? Mutation;
        public void Save(AgentPausedTurn value) => state = value;
        public AgentPausedTurn? Find(string sessionId) => state is not null && state.SessionId == sessionId
            ? state with { DisclosureHistory = ChangeHistory(state.DisclosureHistory, Mutation) } : null;
        public void Remove(string sessionId) { if (state?.SessionId == sessionId) state = null; }
    }

    private sealed class SessionBoundary(IAgentSessionService inner) : IAgentSessionService
    {
        internal string? Mutation;
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindAsync(sessionId, cancellationToken);
        public async Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
            => Project(await inner.AddMessageAsync(sessionId, request, cancellationToken));
        public async Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
            => (await inner.ListMessagesAsync(sessionId, cancellationToken)).Select(Project).ToArray();
        public async Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        { var message = await inner.FindMessageAsync(sessionId, messageId, cancellationToken); return message is null ? null : Project(message); }
        public async Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
        { var message = await inner.FindLatestMessageAsync(sessionId, role, cancellationToken); return message is null ? null : Project(message); }
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
            => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.ListContextAsync(sessionId, cancellationToken);
        private AgentMessage Project(AgentMessage message) => message.Role == AgentRole.Assistant
            ? message with { DisclosureHistory = ChangeHistory(message.DisclosureHistory, Mutation) } : message;
    }
}
