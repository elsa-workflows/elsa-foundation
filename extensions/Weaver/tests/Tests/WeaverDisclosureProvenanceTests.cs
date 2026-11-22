using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverDisclosureProvenanceTests
{
    [Fact]
    public async Task A_legacy_tool_only_user_row_without_disclosure_provenance_cannot_resume_a_delegated_provider()
    {
        var provider = new DelegatedProviderProbe();
        var source = new WeaverTestContextSource("provenance.context", "provenance.module",
            [new("current-context", "provenance.module", "resource-1", "Current resource", "provenance.resource",
                WeaverContextSensitivity.Internal, "selection", "Current authorized evidence", null, new Dictionary<string, string>())
                { ModuleId = "provenance.module" }]);
        using var root = Build(provider, services => services.AddSingleton<IWeaverContextProvider>(source));
        using var scope = root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MetadataDroppingSessionStore>();
        var session = await Session(store, provider.ProviderId);
        var old = await store.AddMessageAsync(session.Id, new(WeaverRole.User, "Read the prior resource using the SDK tool loop.",
            WeaverMessageStatus.Completed, null, [], []) { HasDisclosureProvenance = true });
        // An older store format retained the user row but no tool-result journal or assistant text.
        // Its SDK-side transcript may still contain the old result, so an empty context proves nothing.
        store.LegacyMessageId = old.Id;
        var admitted = await scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()));
        Assert.True(admitted.Succeeded, admitted.Error?.Message);
        var context = admitted.Value!.Attachments;
        await store.AddMessageAsync(session.Id, new(WeaverRole.User, "Explain the current selection.", WeaverMessageStatus.Pending,
            null, context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true });

        var events = await Stream(scope, session.Id);

        Assert.Empty(provider.Inputs);
        var error = Assert.Single(events, item => item.Kind == WeaverStreamEventKind.Error).Error;
        Assert.Equal("weaver.history.provenance_unavailable", error?.Code);
        var projectedOld = Assert.Single(await store.ListMessagesAsync(session.Id), item => item.Id == old.Id);
        Assert.Empty(projectedOld.Context);
        Assert.Empty(projectedOld.ToolResults);
        Assert.False(projectedOld.HasDisclosureProvenance);
        Assert.Equal(WeaverMessageStatus.Failed, Assert.Single(await store.ListMessagesAsync(session.Id), item => item.Id != old.Id).Status);
    }

    [Fact]
    public async Task A_custom_store_dropping_tool_journal_provenance_stops_result_delivery_and_retains_the_once_only_effect()
    {
        var tool = new ProposalMutationTool();
        var provider = new DelegatedProviderProbe(tool);
        using var root = Build(provider, services => services.AddSingleton<IWeaverTool>(tool));
        using var scope = root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MetadataDroppingSessionStore>();
        store.DropToolJournalMetadata = true;
        var session = await Session(store, provider.ProviderId, WeaverPolicy.Default with { AutonomyMode = WeaverAutonomyMode.FullAuto });
        await store.AddMessageAsync(session.Id, new(WeaverRole.User, "Update the owned resource.", WeaverMessageStatus.Pending,
            null, [], []) { HasDisclosureProvenance = true });

        var events = await Stream(scope, session.Id);

        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal("new", tool.Value);
        Assert.Equal("revision-2", tool.Revision);
        var proposalStore = scope.ServiceProvider.GetRequiredService<InMemoryWeaverProposalStore>();
        var proposal = Assert.Single(proposalStore.Proposals.Values);
        var retained = await scope.ServiceProvider.GetRequiredService<IWeaverProposalService>().FindResultAsync(proposal.Id);
        var receipt = Assert.IsType<WeaverEffectReceipt>(retained?.Receipt);
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
        Assert.Contains(events, item => item.Kind == WeaverStreamEventKind.Error && item.Error?.Code == "weaver.result.disclosure_unavailable");

        var retry = await Stream(scope, session.Id);

        Assert.Empty(provider.DeliveredResults);
        Assert.Single(provider.Inputs);
        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal(retained, await scope.ServiceProvider.GetRequiredService<IWeaverProposalService>().FindResultAsync(proposal.Id));
        Assert.Contains(retry, item => item.Kind == WeaverStreamEventKind.Error);
    }

    private static ServiceProvider Build(DelegatedProviderProbe provider, Action<IServiceCollection>? configure = null)
        => WeaverTestServices.Create(services =>
        {
            services.AddSingleton<IWeaverProvider>(provider);
            services.AddSingleton<MetadataDroppingSessionStore>(sp => new(new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp => sp.GetRequiredService<MetadataDroppingSessionStore>()));
            configure?.Invoke(services);
        });

    private static Task<WeaverSession> Session(IWeaverSessionService sessions, string providerId, WeaverPolicy? policy = null)
        => sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", providerId, "explain", null,
            policy ?? WeaverPolicy.Default, new Dictionary<string, string>()));

    private static async Task<List<WeaverStreamEvent>> Stream(IServiceScope scope, string sessionId)
    {
        var events = new List<WeaverStreamEvent>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId))
            events.Add(item);
        return events;
    }

    private sealed class DelegatedProviderProbe(IWeaverTool? tool = null) : IWeaverProvider, IWeaverHarness
    {
        public string ProviderId => "provenance-delegated-probe";
        public WeaverHarnessCapabilities Capabilities => WeaverHarnessCapabilities.Tools;
        public List<WeaverHarnessTurnRequest> Inputs { get; } = [];
        public List<WeaverToolResult> DeliveredResults { get; } = [];

        public async IAsyncEnumerable<WeaverStreamEvent> RunTurnAsync(WeaverHarnessTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(request);
            if (tool is not null)
            {
                var invocation = new WeaverToolInvocation("owned-change-call", tool.Descriptor.Name, request.SessionId, request.ActorId,
                    new Dictionary<string, object?> { ["value"] = "new" }, request.Context)
                { TenantId = request.TenantId, TurnId = request.TurnId, Client = request.Client };
                var dispatch = await request.ToolInvoker!.DispatchAsync(invocation, request.Policy, cancellationToken);
                if (dispatch.Result is { } result)
                    DeliveredResults.Add(result);
            }
            await Task.Yield();
            yield return new("delegated-complete", WeaverStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }

        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The engine must use the delegated harness entry point.");

        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverToolApprovalResult(request.Approved, string.Empty));

        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderDiagnostics(ProviderId, true, "available", WeaverProviderKind.ProviderSdkBinding,
                [WeaverProviderOperation.Chat, WeaverProviderOperation.Streaming], WeaverProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
    }

    /// <summary>A legacy/custom serialization boundary; all session, tool and effect behavior stays real.</summary>
    private sealed class MetadataDroppingSessionStore(IWeaverSessionService inner) : IWeaverSessionService
    {
        public string? LegacyMessageId { get; set; }
        public bool DropToolJournalMetadata { get; set; }
        public List<WeaverMessage> DroppedToolRows { get; } = [];

        public Task<WeaverSession> CreateAsync(WeaverSessionCreateRequest request, CancellationToken cancellationToken = default)
            => inner.CreateAsync(request, cancellationToken);
        public Task<WeaverSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.FindAsync(sessionId, cancellationToken);
        public async Task<WeaverMessage> AddMessageAsync(string sessionId, WeaverMessageCreateRequest request, CancellationToken cancellationToken = default)
        {
            var drop = DropToolJournalMetadata && request.Role == WeaverRole.Tool;
            var stored = await inner.AddMessageAsync(sessionId, drop ? request with { ToolResults = [], HasDisclosureProvenance = false } : request, cancellationToken);
            if (drop) DroppedToolRows.Add(stored);
            return Project(stored);
        }
        public async Task<IReadOnlyCollection<WeaverMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
            => (await inner.ListMessagesAsync(sessionId, cancellationToken)).Select(Project).ToArray();
        public async Task<WeaverMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        {
            var message = await inner.FindMessageAsync(sessionId, messageId, cancellationToken);
            return message is null ? null : Project(message);
        }
        public async Task<WeaverMessage?> FindLatestMessageAsync(string sessionId, WeaverRole? role = null, CancellationToken cancellationToken = default)
        {
            var message = await inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
            return message is null ? null : Project(message);
        }
        public Task SetMessageStatusAsync(string sessionId, string messageId, WeaverMessageStatus status, CancellationToken cancellationToken = default)
            => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default)
            => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<WeaverContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.ListContextAsync(sessionId, cancellationToken);

        private WeaverMessage Project(WeaverMessage message) => message.Id == LegacyMessageId
            ? message with { ContextAttachmentIds = [], Context = [], ToolResults = [], HasDisclosureProvenance = false }
            : message;
    }
}
