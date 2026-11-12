using System.Text.Json;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Publication;

public sealed partial class AgentPublicationTests
{
    // Each playground run is its own workflow execution. The root agent of an activity draft Test Run converses in the
    // exact candidate's workspace, so a later run of the same saved revision continues the conversation explicitly, and an
    // edited revision cannot silently continue the old one.
    [Fact]
    public async Task Playground_runs_of_one_saved_revision_continue_one_conversation_that_another_revision_cannot()
    {
        var model = new HistoryModel();
        await using var fixture = await Fixture.CreateAsync(allowContinuity: true, invocationControls: true, configureRuntime: services =>
        {
            new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services);
            new Elsa.Activities.Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services);
            services.AddSingleton<IAgentModelClientFactory>(model);
            services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            services.AddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = PersistenceMode.Balanced });
            services.AddPersistenceModes();
        });
        var provider = fixture.Services;
        var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(JsonElement), "System.Text.Json.JsonElement");
        await new AgentTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
        IActivityDraftTestRunService service = new ActivityDraftTestRunService(fixture.Stores, fixture.Stores, fixture.Stores, fixture.Compiler, new Access(),
            fixture.Templates, fixture.Sources,
            new ActivityTemplatePlacer(fixture.Stores, fixture.Templates, fixture.Sources, new Sha256ActivityPlacementHasher(), new DefaultActivityStructureService([]),
                new RuntimeInputBindingCompiler(types), new RuntimeOutputCaptureCompiler(fixture.Drivers)),
            new WorkflowExecutableHasher(), provider.GetRequiredService<IWorkflowExecutableStore>(),
            provider.GetRequiredService<IWorkflowStartDispatcher>(), provider.GetRequiredService<IWorkflowTestScopeStore>(),
            provider.GetRequiredService<IWorkflowExecutionPartitionAccessor>(), new InMemoryActivityDraftTestRunStore(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>(), provider.GetRequiredService<IActivityExecutionStateStore>(),
            new DefaultActivityDraftTestRunCancellationPolicy(), provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(), TimeProvider.System);

        var first = await RunAsync(provider, service, "fresh", 1, null);
        Assert.Equal("Completed", first.Run.Status);
        var conversation = first.Output.GetProperty("Conversation");
        var conversationId = conversation.GetProperty("conversationId").GetString()!;
        Assert.Equal(1, conversation.GetProperty("revision").GetInt64());
        // The runtime pins the generic Test Run's source; its kind is what the binding recognizes as a playground run.
        var pinned = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(first.Run.WorkflowExecutionId))!;
        Assert.Equal((WorkflowRunKind.TestRun, AgentNativeBinding.ActivityDraftTestRunSourceKind, "draft"),
            (pinned.RunKind, pinned.PinnedSource?.SourceKind, pinned.PinnedSource?.SourceId));
        // Only that exact provenance grants the candidate workspace: another run kind or source, or a nested invocation, stays workflow-owned.
        var root = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(first.Run.WorkflowExecutionId)).Single(x => x.ParentActivityExecutionId is null);
        Assert.Equal("activity-draft:draft", AgentNativeBinding.ConversationWorkspace(pinned, root));
        Assert.Null(AgentNativeBinding.ConversationWorkspace(pinned with { RunKind = WorkflowRunKind.PublishedRun }, root));
        Assert.Null(AgentNativeBinding.ConversationWorkspace(pinned with { PinnedSource = pinned.PinnedSource! with { SourceKind = "WorkflowDraftSnapshot" } }, root));
        Assert.Null(AgentNativeBinding.ConversationWorkspace(pinned, root with { ParentActivityExecutionId = "parent" }));

        var second = await RunAsync(provider, service, "continue", 1, new { conversationId, revision = 1 });
        Assert.Equal("Completed", second.Run.Status);
        Assert.NotEqual(first.Run.WorkflowExecutionId, second.Run.WorkflowExecutionId);
        Assert.Equal(conversationId, second.Output.GetProperty("Conversation").GetProperty("conversationId").GetString());
        Assert.Equal(2, second.Output.GetProperty("Conversation").GetProperty("revision").GetInt64());
        Assert.Equal(new[] { 1, 3 }, model.RequestSizes);
        var evidence = (await provider.GetRequiredService<IAgentInvocationInspector>().InspectAsync(second.Run.WorkflowExecutionId, second.Run.OuterActivityExecutionId!))!;
        Assert.Equal((conversationId, 1L, (long?)2), (evidence.Turn!.Conversation!.ConversationId, evidence.Turn.Conversation.BaseRevision, evidence.Turn.Conversation.CurrentRevision));

        // A stale revision of the same candidate conversation is refused without a model call.
        var stale = await RunAsync(provider, service, "stale", 1, new { conversationId, revision = 1 });
        await AssertConflictAsync(provider, stale.Run);
        Assert.Equal(2, model.RequestSizes.Count);

        // Editing the draft makes a new candidate; its playground run cannot continue the previous revision's conversation.
        var changed = await fixture.Stores.ExecuteAsync(new ReplaceActivityDraftRequest("draft", 1,
            await fixture.StateAsync(fixture.Manifest with { Instructions = "Write the supplied brief, briefly." }), []));
        Assert.Equal(2, changed.Revision);
        var edited = await RunAsync(provider, service, "edited", 2, new { conversationId, revision = 2 });
        await AssertConflictAsync(provider, edited.Run);
        Assert.Equal(2, model.RequestSizes.Count);
        var fresh = await RunAsync(provider, service, "edited-fresh", 2, null);
        Assert.Equal("Completed", fresh.Run.Status);
        Assert.NotEqual(conversationId, fresh.Output.GetProperty("Conversation").GetProperty("conversationId").GetString());
        Assert.Equal(new[] { 1, 3, 1 }, model.RequestSizes);
    }

    // The refusal is the journal's own conversation conflict, raised at claim before any model request.
    private static async Task AssertConflictAsync(IServiceProvider provider, ActivityDraftTestRunView run)
    {
        Assert.Equal("Faulted", run.Status);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        Assert.Equal("agent.conversation.conflict", states.Single(x => x.ParentActivityExecutionId is not null).Fault?.Code);
        var turn = (await provider.GetRequiredService<IAgentInvocationInspector>().InspectAsync(run.WorkflowExecutionId, run.OuterActivityExecutionId!))!.Turn!;
        Assert.Empty(turn.ModelRequests);
    }

    private static async Task<(ActivityDraftTestRunView Run, JsonElement Output)> RunAsync(IServiceProvider provider, IActivityDraftTestRunService service,
        string key, long revision, object? continuation)
    {
        // A fresh run passes empty options: the generic Test Run binds every input it is given and none it is not.
        var inputs = new Dictionary<string, ActivityDraftTestRunInput>
        {
            ["brief-key"] = new("Present", Json("\"moss gardens\"")),
            ["$agent.options"] = new("Present", JsonSerializer.SerializeToElement(continuation ?? new { }))
        };
        var started = await service.StartAsync(new StartActivityDraftTestRun("draft", revision, "continuity-" + key, inputs));
        Assert.Null(started.Failure);
        var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(started.WorkflowExecutionId,
            WorkflowExecutionActorActivationReason.SchedulerWork, DateTimeOffset.UtcNow, "continuity-test", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var workflows = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        for (var step = 0; step < 16 && (await workflows.FindAsync(started.WorkflowExecutionId))!.Status == WorkflowExecutionStatus.Running; step++)
        {
            var work = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("continuity-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
            if (work is not null) await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
            var now = DateTimeOffset.UtcNow;
            await actor.EnqueueAsync(new($"drain-{key}-{step}", started.WorkflowExecutionId,
                new($"drain-{key}-{step}", started.WorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()),
                $"drain-{key}-{step}", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        }
        var run = await service.GetAsync(started.TestRunId);
        var root = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(started.WorkflowExecutionId)).Single(x => x.ParentActivityExecutionId is null);
        return (run, root.Completion?.Result.InlineValue ?? default);
    }

    private sealed class HistoryModel : IAgentModelClientFactory
    {
        public string ProviderKey => "fixture";
        public List<int> RequestSizes { get; } = [];
        public ValueTask<AgentModelClientLease> OpenAsync(AgentModelOpenRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentModelClientLease(new Client(this, request), new() { MaxOutputTokens = 4096 }, "fixture/1", 1)
            { EnforcesDispatchAuthority = request.DispatchAuthority is not null });
        private sealed class Client(HistoryModel owner, AgentModelOpenRequest request) : IChatClient
        {
            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                if (request.DispatchAuthority is { } authority)
                    await authority.BeforeSendAsync(new("fixture", request.Descriptor.Model.ModelId, "fixture-incarnation", 1, "fixture/1", "continuity/1"), cancellationToken);
                var sent = messages.ToArray();
                owner.RequestSizes.Add(sent.Length);
                return new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new { Article = $"turn {owner.RequestSizes.Count}" })))
                { Usage = new() { InputTokenCount = 5, OutputTokenCount = 3 } };
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }
}
