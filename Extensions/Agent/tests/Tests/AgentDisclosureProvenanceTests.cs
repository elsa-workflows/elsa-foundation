using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentDisclosureProvenanceTests
{
    [Fact]
    public async Task A_legacy_tool_only_user_row_without_disclosure_provenance_cannot_resume_a_delegated_provider()
    {
        var provider = new DelegatedProviderProbe();
        var source = new AgentTestContextSource("provenance.context", "provenance.module",
            [new("current-context", "provenance.module", "resource-1", "Current resource", "provenance.resource",
                AgentContextSensitivity.Internal, "selection", "Current authorized evidence", null, new Dictionary<string, string>())
                { ModuleId = "provenance.module" }]);
        using var root = Build(provider, services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MetadataDroppingSessionStore>();
        var session = await Session(store, provider.ProviderId);
        var old = await store.AddMessageAsync(session.Id, new(AgentRole.User, "Read the prior resource using the SDK tool loop.",
            AgentMessageStatus.Completed, null, [], []) { HasDisclosureProvenance = true });
        // An older store format retained the user row but no tool-result journal or assistant text.
        // Its SDK-side transcript may still contain the old result, so an empty context proves nothing.
        store.LegacyMessageId = old.Id;
        var admitted = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()));
        Assert.True(admitted.Succeeded, admitted.Error?.Message);
        var context = admitted.Value!.Attachments;
        await store.AddMessageAsync(session.Id, new(AgentRole.User, "Explain the current selection.", AgentMessageStatus.Pending,
            null, context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true });

        var events = await Stream(scope, session.Id);

        Assert.Empty(provider.Inputs);
        var error = Assert.Single(events, item => item.Kind == AgentStreamEventKind.Error).Error;
        Assert.Equal("agent.history.provenance_unavailable", error?.Code);
        var projectedOld = Assert.Single(await store.ListMessagesAsync(session.Id), item => item.Id == old.Id);
        Assert.Empty(projectedOld.Context);
        Assert.Empty(projectedOld.ToolResults);
        Assert.False(projectedOld.HasDisclosureProvenance);
        Assert.Equal(AgentMessageStatus.Failed, Assert.Single(await store.ListMessagesAsync(session.Id), item => item.Id != old.Id).Status);
    }

    [Fact]
    public async Task A_custom_store_dropping_tool_journal_provenance_stops_result_delivery_and_retains_the_once_only_effect()
    {
        var tool = new ProposalMutationTool();
        var provider = new DelegatedProviderProbe(tool);
        using var root = Build(provider, services => services.AddSingleton<IAgentTool>(tool));
        using var scope = root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MetadataDroppingSessionStore>();
        store.DropToolJournalMetadata = true;
        var session = await Session(store, provider.ProviderId, AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        await store.AddMessageAsync(session.Id, new(AgentRole.User, "Update the owned resource.", AgentMessageStatus.Pending,
            null, [], []) { HasDisclosureProvenance = true });

        var events = await Stream(scope, session.Id);

        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal("new", tool.Value);
        Assert.Equal("revision-2", tool.Revision);
        var proposalStore = scope.ServiceProvider.GetRequiredService<InMemoryAgentProposalStore>();
        var proposal = Assert.Single(proposalStore.Proposals.Values);
        var retained = await scope.ServiceProvider.GetRequiredService<IAgentProposalService>().FindResultAsync(proposal.Id);
        var receipt = Assert.IsType<AgentEffectReceipt>(retained?.Receipt);
        Assert.Equal("revision-1", receipt.Before.Revision);
        Assert.Equal(tool.Revision, receipt.After.Revision);
        Assert.Equal(tool.Value, receipt.Evidence.Data.GetProperty("value").GetString());
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, receipt.InvocationId);
        var dropped = Assert.Single(store.DroppedToolRows);
        Assert.Empty(dropped.ToolResults);
        Assert.False(dropped.HasDisclosureProvenance);
        Assert.False(string.IsNullOrWhiteSpace(dropped.DeliveryFingerprint));
        Assert.Single(provider.Inputs);
        Assert.Empty(provider.DeliveredResults);
        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error && item.Error?.Code == "agent.result.disclosure_unavailable");

        var retry = await Stream(scope, session.Id);

        Assert.Empty(provider.DeliveredResults);
        Assert.Single(provider.Inputs);
        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal(retained, await scope.ServiceProvider.GetRequiredService<IAgentProposalService>().FindResultAsync(proposal.Id));
        Assert.Contains(retry, item => item.Kind == AgentStreamEventKind.Error);
    }

    private static ServiceProvider Build(DelegatedProviderProbe provider, Action<IServiceCollection>? configure = null)
        => AgentTestServices.Create(services =>
        {
            services.AddSingleton<IAgentProvider>(provider);
            services.AddSingleton<MetadataDroppingSessionStore>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<MetadataDroppingSessionStore>()));
            configure?.Invoke(services);
        });

    private static Task<AgentSession> Session(IAgentSessionService sessions, string providerId, AgentPolicy? policy = null)
        => sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", providerId, "explain", null,
            policy ?? AgentPolicy.Default, new Dictionary<string, string>()));

    private static async Task<List<AgentStreamEvent>> Stream(IServiceScope scope, string sessionId)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId))
            events.Add(item);
        return events;
    }

    private sealed class DelegatedProviderProbe(IAgentTool? tool = null) : IAgentProvider, IAgentHarness
    {
        public string ProviderId => "provenance-delegated-probe";
        public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools;
        public List<AgentHarnessTurnRequest> Inputs { get; } = [];
        public List<AgentToolResult> DeliveredResults { get; } = [];

        public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(request);
            if (tool is not null)
            {
                var invocation = new AgentToolInvocation("owned-change-call", tool.Descriptor.Name, request.SessionId, request.ActorId,
                    new Dictionary<string, object?> { ["value"] = "new" }, request.Context)
                { TenantId = request.TenantId, TurnId = request.TurnId, Client = request.Client };
                var dispatch = await request.ToolInvoker!.DispatchAsync(invocation, request.Policy, cancellationToken);
                if (dispatch.Result is { } result)
                    DeliveredResults.Add(result);
            }
            await Task.Yield();
            yield return new("delegated-complete", AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The engine must use the delegated harness entry point.");

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat, AgentProviderOperation.Streaming], AgentProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
    }

    /// <summary>A legacy/custom serialization boundary; all session, tool and effect behavior stays real.</summary>
    private sealed class MetadataDroppingSessionStore(IAgentSessionService inner) : IAgentSessionService
    {
        public string? LegacyMessageId { get; set; }
        public bool DropToolJournalMetadata { get; set; }
        public List<AgentMessage> DroppedToolRows { get; } = [];

        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default)
            => inner.CreateAsync(request, cancellationToken);
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.FindAsync(sessionId, cancellationToken);
        public async Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
        {
            var drop = DropToolJournalMetadata && request.Role == AgentRole.Tool;
            var stored = await inner.AddMessageAsync(sessionId, drop ? request with { ToolResults = [], HasDisclosureProvenance = false } : request, cancellationToken);
            if (drop) DroppedToolRows.Add(stored);
            return Project(stored);
        }
        public async Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
            => (await inner.ListMessagesAsync(sessionId, cancellationToken)).Select(Project).ToArray();
        public async Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        {
            var message = await inner.FindMessageAsync(sessionId, messageId, cancellationToken);
            return message is null ? null : Project(message);
        }
        public async Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
        {
            var message = await inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
            return message is null ? null : Project(message);
        }
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
            => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.ListContextAsync(sessionId, cancellationToken);

        private AgentMessage Project(AgentMessage message) => message.Id == LegacyMessageId
            ? message with { ContextAttachmentIds = [], Context = [], ToolResults = [], HasDisclosureProvenance = false }
            : message;
    }
}
