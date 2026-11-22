using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverTurnDisclosureTests
{
    [Fact]
    public async Task Review_digest_binds_the_module_declared_disclosure_owner()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        var prepared = Assert.IsType<WeaverPreparedToolInvocation>(proposal.PreparedInvocation);

        Assert.NotEqual(WeaverInvocationIntegrity.PreparedDigest(prepared),
            WeaverInvocationIntegrity.PreparedDigest(prepared with { DisclosureSourceId = "another-installed-source" }));
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    [Fact]
    public async Task A_module_can_report_creation_from_a_collection_to_its_new_resource_without_generic_same_resource_assumptions()
    {
        var tool = new CreateResourceTool();
        var provider = new CapturingProvider();
        using var root = Build(new CreationSource(tool), provider, services => services.AddSingleton<IWeaverTool>(tool));
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
        var session = await Session(sessions);
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "mutate by creating the requested resource", WeaverMessageStatus.Pending, null, [], []));
        var first = await Collect(scope, session.Id);
        var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(first, x => x.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);

        var resumed = await Collect(scope, session.Id);

        Assert.Equal(WeaverStreamEventKind.Completed, resumed[^1].Kind);
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
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await Session(sessions);
        await Post(scope, session.Id, "explain");
        source.Allowed = false;

        var events = await Collect(scope, session.Id);

        Assert.Empty(provider.Inputs);
        Assert.Equal("weaver.context.incomplete", Assert.Single(events, x => x.Kind == WeaverStreamEventKind.Error).Error?.Code);
        var admitted = Assert.IsType<WeaverContextContribution>(Assert.Single(events, x => x.Kind == WeaverStreamEventKind.ContextAdmitted).Payload);
        Assert.Empty(admitted.Attachments);
        Assert.True(Assert.Single(admitted.Diagnostics).Required);
        Assert.Equal(WeaverMessageStatus.Failed, Assert.Single(await sessions.ListMessagesAsync(session.Id)).Status);
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "another request", WeaverMessageStatus.Pending, null, [], []));
    }

    [Fact]
    public async Task Historical_resource_derived_assistant_text_requires_current_owner_authority_even_when_new_prompt_has_no_context()
    {
        var source = new Source();
        var provider = new CapturingProvider();
        using var root = Build(source, provider);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await Session(sessions);
        await Post(scope, session.Id, "explain");
        Assert.Equal(WeaverStreamEventKind.Completed, (await Collect(scope, session.Id))[^1].Kind);
        var assistant = Assert.Single(await sessions.ListMessagesAsync(session.Id), x => x.Role == WeaverRole.Assistant && x.Content.Length > 0);
        Assert.Equal(source.Descriptor.Id, Assert.Single(assistant.Context).ContributorId);
        Assert.Single(provider.Inputs);
        source.Allowed = false;
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "continue discussion", WeaverMessageStatus.Pending, null, [], []));

        var denied = await Collect(scope, session.Id);

        Assert.Single(provider.Inputs);
        Assert.Contains(denied, x => x.Kind == WeaverStreamEventKind.Error);
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
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await Session(sessions);
        var attachment = Source.Attachment() with { ContributorId = defaultHook ? "source" : "removed-source" };
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "explain", WeaverMessageStatus.Pending, null, [attachment.Id], [attachment]));

        var events = await Collect(scope, session.Id);

        Assert.Empty(provider.Inputs);
        Assert.Contains(events, x => x.Kind == WeaverStreamEventKind.Error);
    }

    [Fact]
    public async Task Accepted_effect_receipt_stays_true_when_current_tool_authority_blocks_continuation()
    {
        var tool = new ApplyChangeTool();
        var authority = new Authority();
        var provider = new CapturingProvider();
        using var root = Build(new Source(), provider, services =>
        {
            services.AddSingleton<IWeaverTool>(tool);
            services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => authority));
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
        var session = await Session(sessions);
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "mutate the resource", WeaverMessageStatus.Pending, null, [], []));
        var first = await Collect(scope, session.Id);
        var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(first, x => x.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var execution = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(execution.Succeeded);
        var stored = await proposals.FindResultAsync(proposal.Id);
        Assert.NotNull(stored?.Receipt);
        var callsBefore = provider.Inputs.Count;
        authority.Allowed = false;

        var denied = await Collect(scope, session.Id);

        Assert.Equal(callsBefore, provider.Inputs.Count);
        Assert.Equal("weaver.result.disclosure_denied", Assert.Single(denied, x => x.Kind == WeaverStreamEventKind.Error).Error?.Code);
        Assert.Equal(stored, await proposals.FindResultAsync(proposal.Id));
        Assert.Equal(1, tool.Invocations);
        Assert.Contains(await sessions.ListMessagesAsync(session.Id), x => x.Role == WeaverRole.Tool
            && x.ToolResults.Any(value => value.Receipt == stored!.Receipt));
    }

    private static ServiceProvider Build(IWeaverContextProvider source, CapturingProvider provider, Action<IServiceCollection>? configure = null)
        => WeaverTestServices.Create(services =>
        {
            services.AddSingleton<IWeaverProvider>(provider);
            services.AddScoped<IWeaverContextProvider>(_ => source);
            configure?.Invoke(services);
        });

    private static Task<WeaverSession> Session(IWeaverSessionService sessions)
        => sessions.CreateAsync(new("tenant", "actor", "conversation", DeterministicWeaverProvider.Id, "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

    private static async Task Post(IServiceScope scope, string sessionId, string text)
    {
        var result = await scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>().CollectAsync(WeaverPolicy.Default,
            new(sessionId, "*", new Dictionary<string, string>()));
        Assert.True(result.Succeeded);
        var context = result.Value!.Attachments;
        await scope.ServiceProvider.GetRequiredService<IWeaverSessionService>().AddMessageAsync(sessionId,
            new(WeaverRole.User, text, WeaverMessageStatus.Pending, null, context.Select(x => x.Id).ToArray(), context));
    }

    private static async Task<List<WeaverStreamEvent>> Collect(IServiceScope scope, string sessionId)
    {
        var events = new List<WeaverStreamEvent>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId)) events.Add(item);
        return events;
    }

    private sealed class Source : IWeaverContextProvider
    {
        public bool Allowed { get; set; } = true;
        public int HistoricalChecks { get; private set; }
        public WeaverContextProviderDescriptor Descriptor { get; } = new("source", "resource.module", "*", ["resource.context"], ["1"], ["resource"]);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution([Attachment() with { ContributorId = "forged-source" }], []));
        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (request.IsHistorical) HistoricalChecks++;
            return ValueTask.FromResult(Allowed ? new WeaverContextContribution(request.Context, [])
                : new([], [new(Descriptor.Id, "resource.permission_denied", "Current read authority is required.", true)]));
        }
        public static WeaverContextAttachment Attachment() => new("resource-context", "resource.module", "resource", "Resource", "resource.context",
            WeaverContextSensitivity.Internal, "resource", "Authorized resource summary", null, new Dictionary<string, string>())
        { ModuleId = "resource.module", SchemaVersion = "1", Target = new("resource.module", "resource", "resource", "revision-1") };
    }

    private sealed class UnsupportedSource : IWeaverContextProvider
    {
        public WeaverContextProviderDescriptor Descriptor { get; } = new("source", "resource.module", "*", ["resource.context"], ["1"], ["resource"]);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution([], []));
    }

    private sealed class CreateResourceTool : IWeaverTool
    {
        public int Executions { get; private set; }
        public string? CreatedResourceId { get; private set; }
        public WeaverToolDescriptor Descriptor { get; } = new("apply-change", "Create owned item", "Creates an item in the module-owned collection.",
            WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired, "{}", [], [])
        { ModuleId = "creation.module", DisclosureSourceId = "creation.source" };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(new(new("creation.module", "collection", null, "revision-1"),
                JsonSerializer.SerializeToElement(new { }), WeaverRisk.ReviewRequired, new("creation.preview", "1", JsonSerializer.SerializeToElement(new { })), true)));
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            CreatedResourceId = Guid.NewGuid().ToString("N");
            var observed = new WeaverPayload("creation.item", "1", JsonSerializer.SerializeToElement(new { id = CreatedResourceId }));
            return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Created the module-owned item.", observed)
            { Receipt = new(invocation.InvocationId, invocation.Target!, new("creation.module", "item", CreatedResourceId, "revision-1"), WeaverEffectScope.ServerPersisted, observed) }));
        }
    }

    private sealed class CreationSource(CreateResourceTool tool) : IWeaverContextProvider
    {
        public WeaverContextProviderDescriptor Descriptor { get; } = new("creation.source", "creation.module", "creation", [], ["1"], ["collection", "item"]);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution([], []));
        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(request.Results.All(x => x.Receipt is { } receipt && receipt.After.ResourceId == tool.CreatedResourceId
                    && !string.IsNullOrEmpty(tool.CreatedResourceId) && x.Payload?.Data.GetProperty("id").GetString() == tool.CreatedResourceId)
                ? new WeaverContextContribution([], [])
                : new([], [new(Descriptor.Id, "creation.not_owned", "The result does not identify an observed item in this module.", true)]));
    }

    private sealed class Authority : IWeaverToolAuthorizer
    {
        public bool Allowed { get; set; } = true;
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverPolicyDecision(Allowed, Allowed ? [] : [new("test.denied", "Current authority denied.")]));
    }

    private sealed class CapturingProvider : IWeaverProvider
    {
        private readonly DeterministicWeaverProvider _inner = new();
        public string ProviderId => _inner.ProviderId;
        public List<WeaverTurnContext> Inputs { get; } = [];
        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);
        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken)) yield return item;
        }
        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
    }
}
