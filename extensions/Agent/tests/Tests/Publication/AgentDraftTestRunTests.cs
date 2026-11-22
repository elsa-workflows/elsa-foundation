using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
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
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Agent.Tests.Publication;

public sealed partial class AgentPublicationTests
{
    // Calls the actual application service/dispatcher/worker, not an HTTP endpoint. Store tuple and
    // persistence modes are explicitly composed; transport permissions are a separate Studio gate.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generic_draft_Test_Run_service_uses_the_agent_production_worker_and_exact_candidate(bool streaming)
    {
        var model = new DraftRuntimeModel();
        var capture = new RecordingCapturePolicy();
        await using var fixture = await Fixture.CreateAsync(configureRuntime: services =>
        {
            services.RemoveAll<IRuntimePayloadCapturePolicy>();
            services.AddSingleton<IRuntimePayloadCapturePolicy>(capture);
            new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services);
            new Elsa.Activities.Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services);
            services.AddSingleton<IAgentModelClientFactory>(model);
            services.AddSingleton(new AgentStreamingPolicy(streaming));
            services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            services.AddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = PersistenceMode.Balanced });
            services.AddPersistenceModes();
        });
        var provider = fixture.Services;
        var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(System.Text.Json.JsonElement), "System.Text.Json.JsonElement");
        await new AgentTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
        var dispatcher = new ObservedStartDispatcher(provider.GetRequiredService<IWorkflowStartDispatcher>());
        IActivityDraftTestRunService service = new ActivityDraftTestRunService(fixture.Stores, fixture.Stores, fixture.Stores, fixture.Compiler, new Access(),
            fixture.Templates, fixture.Sources,
            new ActivityTemplatePlacer(fixture.Stores, fixture.Templates, fixture.Sources, new Sha256ActivityPlacementHasher(), new DefaultActivityStructureService([]),
                new RuntimeInputBindingCompiler(types), new RuntimeOutputCaptureCompiler(fixture.Drivers)),
            new Elsa.Workflows.Publishing.Services.WorkflowExecutableHasher(), provider.GetRequiredService<IWorkflowExecutableStore>(),
            dispatcher, provider.GetRequiredService<IWorkflowTestScopeStore>(),
            provider.GetRequiredService<IWorkflowExecutionPartitionAccessor>(), new InMemoryActivityDraftTestRunStore(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>(), provider.GetRequiredService<IActivityExecutionStateStore>(),
            new DefaultActivityDraftTestRunCancellationPolicy(), provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(), TimeProvider.System);
        var request = new StartActivityDraftTestRun("draft", 1, "agent-draft-test", new Dictionary<string, ActivityDraftTestRunInput>
        { ["brief-key"] = new("Present", Json("\"moss gardens\"")) });
        var started = await service.StartAsync(request);
        Assert.Null(dispatcher.Failure);
        Assert.Null(started.Failure);
        Assert.NotNull(started.ArtifactId);
        Assert.NotNull(started.SourceReferenceId);
        Assert.Equal(0, model.Calls);
        var candidate = (await provider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(started.ArtifactId!))!;
        Assert.StartsWith("activity-test-version-", candidate.RootActivity.DescriptorPayload.GetProperty("definitionVersionId").GetString());
        Assert.Equal("draft-1", candidate.RootActivity.DescriptorPayload.GetProperty("version").GetString());
        Assert.Equal("Write the supplied brief.", candidate.RootActivity.DescriptorPayload.GetProperty("instructions").GetString());
        var changed = await fixture.Stores.ExecuteAsync(new ReplaceActivityDraftRequest("draft", 1,
            await fixture.StateAsync(fixture.Manifest with { Instructions = "Changed after scheduling; must not execute." }), []));
        Assert.Equal(2, changed.Revision);
        var work = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("draft-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(work);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
        var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(started.WorkflowExecutionId, WorkflowExecutionActorActivationReason.SchedulerWork, DateTimeOffset.UtcNow, "draft-test", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var now = DateTimeOffset.UtcNow;
        await actor.EnqueueAsync(new("draft-test-drain", started.WorkflowExecutionId,
            new("draft-test-drain", started.WorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()),
            "draft-test-drain", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        var completed = await service.GetAsync(started.TestRunId);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(started.ArtifactId, completed.ArtifactId);
        Assert.Equal(started.SourceReferenceId, completed.SourceReferenceId);
        Assert.Equal(1, model.Calls);
        Assert.Equal(streaming, model.Streamed);
        Assert.Equal("Write the supplied brief.", model.Instructions);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(started.WorkflowExecutionId);
        var root = states.Single(x => x.ParentActivityExecutionId is null);
        Assert.Equal("draft about moss gardens", root.Completion!.Result.InlineValue!.Value.GetProperty("Article").GetString());
        await AssertEvidenceAsync(provider.GetRequiredService<IAgentInvocationInspector>(), capture,
            provider.GetRequiredService<IActivityExecutionStateStore>(), completed, candidate, root, states);
        var replay = await service.StartAsync(request);
        Assert.Equal(completed.TestRunId, replay.TestRunId);
        Assert.Equal("Completed", replay.Status);
        Assert.Equal(1, replay.DraftRevision);
        var stale = await service.StartAsync(request with { IdempotencyKey = "new-key-with-stale-revision" });
        Assert.Equal("ValidationRejected", stale.Status);
        Assert.Equal("activity.draft.stale-revision", stale.Failure!.Code);
        Assert.Equal(1, model.Calls);
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("no-stale-work", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
    }

    // The evidence of the exact candidate's accepted turn, read through the same native identity, without protected material.
    private static async Task AssertEvidenceAsync(IAgentInvocationInspector inspector, RecordingCapturePolicy capture, IActivityExecutionStateStore states,
        ActivityDraftTestRunView run, WorkflowExecutable candidate, ActivityExecutionState root, IReadOnlyCollection<ActivityExecutionState> executed)
    {
        Assert.Equal(root.InvocationId, run.OuterActivityExecutionId);
        capture.Requests.Clear();
        var evidence = (await inspector.InspectAsync(run.WorkflowExecutionId, root.InvocationId))!;
        Assert.Equal(root.InvocationId, evidence.ActivityExecutionId);
        Assert.Equal(candidate.RootActivity.DescriptorPayload.GetProperty("definitionVersionId").GetString(), evidence.Agent.DefinitionVersionId);
        Assert.Equal("draft-1", evidence.Agent.Version);
        Assert.Equal(run.ArtifactId, evidence.Agent.ArtifactId);
        Assert.Equal("Completed", evidence.ActivityStatus);
        Assert.Null(evidence.Fault);
        Assert.Equal("Payload", evidence.Output!.CaptureMode);
        Assert.Equal("draft about moss gardens", evidence.Output.Value!.Value.GetProperty("Article").GetString());
        var asked = Assert.Single(capture.Requests);
        Assert.Equal((RuntimePayloadCaptureSubject.ActivityOutput, run.WorkflowExecutionId, root.InvocationId, false),
            (asked.Subject, asked.WorkflowExecutionId, asked.ActivityExecutionId, asked.IsSensitive));
        var turn = evidence.Turn!;
        Assert.Equal(AgentTurnStatus.Accepted, turn.Status);
        Assert.Null(turn.FailureCode);
        // The native completion is committed, but the settlement that records its application is a post-commit intent this
        // test does not dispatch: accepted and applied stay separate facts.
        Assert.Equal(AgentTurnApplicationStatus.SettlementPending, turn.Application);
        Assert.Single(turn.ModelRequests);
        Assert.Equal(5, turn.Usage!.InputTokens);
        Assert.Equal(3, turn.Usage.OutputTokens);
        Assert.Null(turn.Conversation);
        var wire = System.Text.Json.JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("Write the supplied brief.", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("protected", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fence", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixture-incarnation", wire, StringComparison.Ordinal);

        // The host's capture policy, not the inspector, decides whether the value leaves the runtime.
        capture.Mode = RuntimePayloadCaptureMode.None;
        var withheld = (await inspector.InspectAsync(run.WorkflowExecutionId, root.InvocationId))!.Output!;
        Assert.Equal(("None", null), (withheld.CaptureMode, withheld.Value));

        // A sensitive result stays withheld even under a policy that would capture it: the inspector's own rule holds.
        var completion = root.Completion!;
        var sensitive = ValueEnvelope.Inline(completion.Result.Type, completion.Result.InlineValue!.Value,
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true));
        await states.SaveAsync(root with { Completion = new(completion.InvocationId, completion.AttemptId, sensitive, completion.OutcomeKey, completion.CompletedAt, completion.ContractFingerprint) });
        capture.Mode = RuntimePayloadCaptureMode.Payload;
        capture.IgnoreSensitivity = true;
        capture.Requests.Clear();
        var hidden = (await inspector.InspectAsync(run.WorkflowExecutionId, root.InvocationId))!.Output!;
        Assert.Equal(("Payload", null), (hidden.CaptureMode, hidden.Value));
        Assert.True(Assert.Single(capture.Requests).IsSensitive);

        // Only the exact agent boundary in its own workflow has evidence: not its model-turn child, nor a guessed identity.
        var child = executed.Single(x => x.ParentActivityExecutionId == root.InvocationId);
        Assert.Null(await inspector.InspectAsync(run.WorkflowExecutionId, child.InvocationId));
        Assert.Null(await inspector.InspectAsync(run.WorkflowExecutionId, "not-an-activity"));
        Assert.Null(await inspector.InspectAsync("not-a-workflow", root.InvocationId));
    }

    private sealed class RecordingCapturePolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureMode Mode { get; set; } = RuntimePayloadCaptureMode.Payload;
        public bool IgnoreSensitivity { get; set; }
        public List<RuntimePayloadCaptureRequest> Requests { get; } = [];
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request)
        {
            Requests.Add(request);
            return new(request.IsSensitive && !IgnoreSensitivity ? RuntimePayloadCaptureMode.None : Mode, "test");
        }
    }

    private sealed class ObservedStartDispatcher(IWorkflowStartDispatcher inner) : IWorkflowStartDispatcher
    {
        public Exception? Failure { get; private set; }
        public bool CanDispatchSavedStimulus(IStimulusDeliveryStore sources, IWorkflowExecutableStore material, IWorkflowExecutionStateStore executions) =>
            inner.CanDispatchSavedStimulus(sources, material, executions);
        public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            try { return await inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken); }
            catch (Exception exception) { Failure = exception; throw; }
        }
    }

    private sealed class DraftRuntimeModel : IAgentModelClientFactory
    {
        public string ProviderKey => "fixture";
        public int Calls { get; private set; }
        public bool Streamed { get; private set; }
        public string? Instructions { get; private set; }
        public ValueTask<AgentModelClientLease> OpenAsync(AgentModelOpenRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentModelClientLease(new Client(this, request), new() { MaxOutputTokens = 4096 }, "fixture/1", 1)
            { EnforcesDispatchAuthority = request.DispatchAuthority is not null, SupportsStreaming = true });
        private sealed class Client(DraftRuntimeModel owner, AgentModelOpenRequest request) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => ResponseAsync(messages, options, cancellationToken);
            private async Task<ChatResponse> ResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
            {
                if (request.DispatchAuthority is { } authority)
                    await authority.BeforeSendAsync(new("fixture", request.Descriptor.Model.ModelId, "fixture-incarnation", 1, "fixture/1", "draft-test/1"), cancellationToken);
                owner.Calls++;
                owner.Instructions = options?.Instructions;
                using var input = System.Text.Json.JsonDocument.Parse(messages.Last(m => m.Role == ChatRole.User).Text);
                return new(new ChatMessage(ChatRole.Assistant, System.Text.Json.JsonSerializer.Serialize(new { Article = "draft about " + input.RootElement.GetProperty("Brief").GetString() })))
                { Usage = new() { InputTokenCount = 5, OutputTokenCount = 3 } };
            }
            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                owner.Streamed = true;
                var response = await ResponseAsync(messages, options, cancellationToken);
                yield return new(ChatRole.Assistant, response.Text);
                yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
                yield return new(ChatRole.Assistant, [new UsageContent(response.Usage!)]);
            }
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }
}
