using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentTurnDisclosureTests
{
    [Fact]
    public async Task Review_digest_binds_the_module_declared_disclosure_owner()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        var prepared = Assert.IsType<AgentPreparedToolInvocation>(proposal.PreparedInvocation);

        Assert.NotEqual(AgentInvocationIntegrity.PreparedDigest(prepared),
            AgentInvocationIntegrity.PreparedDigest(prepared with { DisclosureSourceId = "another-installed-source" }));
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    [Fact]
    public async Task A_module_can_report_creation_from_a_collection_to_its_new_resource_without_generic_same_resource_assumptions()
    {
        var tool = new CreateResourceTool();
        var provider = new CapturingProvider();
        using var root = Build(new CreationSource(tool), provider, services => services.AddSingleton<IAgentTool>(tool));
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var session = await Session(sessions);
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate by creating the requested resource", AgentMessageStatus.Pending, null, [], []));
        var first = await Collect(scope, session.Id);
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);

        var resumed = await Collect(scope, session.Id);

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        var delivered = Assert.Single(provider.Inputs[^1].PendingToolResults);
        Assert.Equal("collection", delivered.Receipt!.Before.ResourceType);
        Assert.Null(delivered.Receipt.Before.ResourceId);
        Assert.Equal("item", delivered.Receipt.After.ResourceType);
        Assert.Equal(tool.CreatedResourceId, delivered.Receipt.After.ResourceId);
        Assert.Equal(tool.CreatedResourceId, delivered.Receipt.Evidence.Data.GetProperty("id").GetString());
        Assert.Equal(1, tool.Executions);
    }

    [Fact]
    public async Task Authority_withdrawn_after_acceptance_refuses_before_any_provider_input_and_releases_the_prompt()
    {
        var source = new Source();
        var provider = new CapturingProvider();
        using var root = Build(source, provider);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await Session(sessions);
        await Post(scope, session.Id, "explain");
        source.Allowed = false;

        var events = await Collect(scope, session.Id);

        Assert.Empty(provider.Inputs);
        Assert.Equal("agent.context.incomplete", Assert.Single(events, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        var admitted = Assert.IsType<AgentContextContribution>(Assert.Single(events, x => x.Kind == AgentStreamEventKind.ContextAdmitted).Payload);
        Assert.Empty(admitted.Attachments);
        Assert.True(Assert.Single(admitted.Diagnostics).Required);
        Assert.Equal(AgentMessageStatus.Failed, Assert.Single(await sessions.ListMessagesAsync(session.Id)).Status);
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "another request", AgentMessageStatus.Pending, null, [], []));
    }

    [Fact]
    public async Task Historical_resource_derived_assistant_text_requires_current_owner_authority_even_when_new_prompt_has_no_context()
    {
        var source = new Source();
        var provider = new CapturingProvider();
        using var root = Build(source, provider);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await Session(sessions);
        await Post(scope, session.Id, "explain");
        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(scope, session.Id))[^1].Kind);
        var assistant = Assert.Single(await sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Assistant && x.Content.Length > 0);
        Assert.Equal(source.Descriptor.Id, Assert.Single(assistant.Context).ContributorId);
        Assert.Single(provider.Inputs);
        source.Allowed = false;
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "continue discussion", AgentMessageStatus.Pending, null, [], []));

        var denied = await Collect(scope, session.Id);

        Assert.Single(provider.Inputs);
        Assert.Contains(denied, x => x.Kind == AgentStreamEventKind.Error);
        Assert.True(source.HistoricalChecks > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_provenance_or_default_unsupported_source_cannot_disclose_retained_evidence(bool defaultHook)
    {
        var provider = new CapturingProvider();
        var source = new Source();
        using var root = Build(defaultHook ? new UnsupportedSource() : source, provider);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await Session(sessions);
        var attachment = Source.Attachment() with { ContributorId = defaultHook ? "source" : "removed-source" };
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "explain", AgentMessageStatus.Pending, null, [attachment.Id], [attachment]));

        var events = await Collect(scope, session.Id);

        Assert.Empty(provider.Inputs);
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.Error);
    }

    [Fact]
    public async Task Accepted_effect_receipt_stays_true_when_current_tool_authority_blocks_continuation()
    {
        var tool = new ApplyChangeTool();
        var authority = new Authority();
        var provider = new CapturingProvider();
        using var root = Build(new Source(), provider, services =>
        {
            services.AddSingleton<IAgentTool>(tool);
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var session = await Session(sessions);
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null, [], []));
        var first = await Collect(scope, session.Id);
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var execution = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(execution.Succeeded);
        var stored = await proposals.FindResultAsync(proposal.Id);
        Assert.NotNull(stored?.Receipt);
        var callsBefore = provider.Inputs.Count;
        authority.Allowed = false;

        var denied = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, provider.Inputs.Count);
        Assert.Equal("agent.result.disclosure_denied", Assert.Single(denied, x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        Assert.Equal(stored, await proposals.FindResultAsync(proposal.Id));
        Assert.Equal(1, tool.Invocations);
        Assert.Contains(await sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Tool
            && x.ToolResults.Any(value => value.Receipt == stored!.Receipt));
    }

    private static ServiceProvider Build(IAgentContextProvider source, CapturingProvider provider, Action<IServiceCollection>? configure = null)
        => AgentTestServices.Create(services =>
        {
            services.AddSingleton<IAgentProvider>(provider);
            services.AddScoped<IAgentContextProvider>(_ => source);
            configure?.Invoke(services);
        });

    private static Task<AgentSession> Session(IAgentSessionService sessions)
        => sessions.CreateAsync(new("tenant", "actor", "conversation", DeterministicAgentProvider.Id, "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    private static async Task Post(IServiceScope scope, string sessionId, string text)
    {
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new(sessionId, "*", new Dictionary<string, string>()));
        Assert.True(result.Succeeded);
        var context = result.Value!.Attachments;
        await scope.ServiceProvider.GetRequiredService<IAgentSessionService>().AddMessageAsync(sessionId,
            new(AgentRole.User, text, AgentMessageStatus.Pending, null, context.Select(x => x.Id).ToArray(), context));
    }

    private static async Task<List<AgentStreamEvent>> Collect(IServiceScope scope, string sessionId)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId)) events.Add(item);
        return events;
    }

    private sealed class Source : IAgentContextProvider
    {
        public bool Allowed { get; set; } = true;
        public int HistoricalChecks { get; private set; }
        public AgentContextProviderDescriptor Descriptor { get; } = new("source", "resource.module", "*", ["resource.context"], ["1"], ["resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment() with { ContributorId = "forged-source" }], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (request.IsHistorical) HistoricalChecks++;
            return ValueTask.FromResult(Allowed ? new AgentContextContribution(request.Context, [])
                : new([], [new(Descriptor.Id, "resource.permission_denied", "Current read authority is required.", true)]));
        }
        public static AgentContextAttachment Attachment() => new("resource-context", "resource.module", "resource", "Resource", "resource.context",
            AgentContextSensitivity.Internal, "resource", "Authorized resource summary", null, new Dictionary<string, string>())
        { ModuleId = "resource.module", SchemaVersion = "1", Target = new("resource.module", "resource", "resource", "revision-1") };
    }

    private sealed class UnsupportedSource : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new("source", "resource.module", "*", ["resource.context"], ["1"], ["resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], []));
    }

    private sealed class CreateResourceTool : IAgentTool
    {
        public int Executions { get; private set; }
        public string? CreatedResourceId { get; private set; }
        public AgentToolDescriptor Descriptor { get; } = new("apply-change", "Create owned item", "Creates an item in the module-owned collection.",
            AgentToolMutability.Mutating, AgentRisk.ReviewRequired, "{}", [], [])
        { ModuleId = "creation.module", DisclosureSourceId = "creation.source" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(new("creation.module", "collection", null, "revision-1"),
                JsonSerializer.SerializeToElement(new { }), AgentRisk.ReviewRequired, new("creation.preview", "1", JsonSerializer.SerializeToElement(new { })), true)));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            CreatedResourceId = Guid.NewGuid().ToString("N");
            var observed = new AgentPayload("creation.item", "1", JsonSerializer.SerializeToElement(new { id = CreatedResourceId }));
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Created the module-owned item.", observed)
            { Receipt = new(invocation.InvocationId, invocation.Target!, new("creation.module", "item", CreatedResourceId, "revision-1"), AgentEffectScope.ServerPersisted, observed) }));
        }
    }

    private sealed class CreationSource(CreateResourceTool tool) : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new("creation.source", "creation.module", "creation", [], ["1"], ["collection", "item"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(request.Results.All(x => x.Receipt is { } receipt && receipt.After.ResourceId == tool.CreatedResourceId
                    && !string.IsNullOrEmpty(tool.CreatedResourceId) && x.Payload?.Data.GetProperty("id").GetString() == tool.CreatedResourceId)
                ? new AgentContextContribution([], [])
                : new([], [new(Descriptor.Id, "creation.not_owned", "The result does not identify an observed item in this module.", true)]));
    }

    private sealed class Authority : IAgentToolAuthorizer
    {
        public bool Allowed { get; set; } = true;
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentPolicyDecision(Allowed, Allowed ? [] : [new("test.denied", "Current authority denied.")]));
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
