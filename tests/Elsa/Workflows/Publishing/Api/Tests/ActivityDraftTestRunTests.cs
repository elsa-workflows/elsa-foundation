using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDraftTestRunTests
{
    [Fact]
    public void Endpoint_and_request_match_the_reviewed_activity_draft_test_run_contract()
    {
        var endpointType = typeof(StartActivityDraftTestRun).Assembly.GetType(
            "Elsa.Workflows.Publishing.Api.Endpoints.ActivityDraftTestRunEndpoint",
            throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = loggerType.GetProperty("Instance")?.GetValue(null)
                     ?? loggerType.GetField("Instance")!.GetValue(null)!;
        var endpoint = (BaseEndpoint)typeof(Factory).GetMethods()
            .Single(x => x.Name == nameof(Factory.Create) && x.IsGenericMethodDefinition &&
                         x.GetParameters() is [var first, var second] &&
                         first.ParameterType == typeof(Action<DefaultHttpContext>) && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType)
            .Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), new object[] { new Sender(), logger }])!;
        endpoint.Configure();

        Assert.Equal("POST", Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal("publishing/activity-drafts/{draftId}/test-runs", Assert.Single(endpoint.Definition.Routes));
        var json = JsonSerializer.Serialize(new StartActivityDraftTestRun(
            "draft-1",
            8,
            new Dictionary<string, ActivityDraftTestRunInput>
            {
                ["order"] = new("Present", JsonSerializer.SerializeToElement(new { id = "order-42" }))
            },
            "designer-test-42"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(
            "{\"expectedRevision\":8,\"inputs\":{\"order\":{\"state\":\"Present\",\"value\":{\"id\":\"order-42\"}}},\"correlationId\":\"designer-test-42\"}",
            json);
        Assert.DoesNotContain("draftId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_test_run_denies_a_foreign_exact_draft_before_compilation_without_disclosure()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var authoring = AuthoringState.Create("tenant-b");
        await using var provider = BuildProvider(
            documents,
            TimeProvider.System,
            authoring,
            new TestActivityPublishingAuthorizationContext("tenant-a"));
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunPublisher>().StartAsync(new(
                AuthoringState.DraftId,
                authoring.Draft.Revision)));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.Empty(exception.Diagnostics);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expiring_test_run_reference_collects_its_unreferenced_activity_template()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var templates = new InMemoryExecutableActivityTemplateStore();
        var workflows = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var template = Template();
        await templates.SaveAsync(template);
        await references.SaveAsync(new(
            "test-ref", template.TemplateId, "ActivityDraft", "draft-1", "8", "definition-1", "draft-version-1", "draft",
            now.AddHours(-1), null, WorkflowExecutableReferenceScope.TestRun, now.AddMinutes(-1)));
        var collector = new WorkflowExecutableReferenceGarbageCollector(
            workflows,
            references,
            templates,
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

        var result = await collector.SweepAsync(now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Fact]
    public async Task Groundwork_sqlite_graph_run_suspends_restarts_in_runtime_only_host_resumes_inspects_and_propagates_output_once()
    {
        var clock = new MutableTimeProvider(new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-restart-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var authoring = AuthoringState.Create();
        ActivityDraftTestRunView first;
        ActivityDraftTestRunView second;
        string templateId;
        IReadOnlyDictionary<string, long> committedExecutionSequences;

        try
        {
            var generation1Documents = await OpenSqliteAsync(connectionString);
            try
            {
                await using var generation1 = BuildProvider(generation1Documents, clock, authoring);
                first = await StartAsync(generation1, authoring.Draft.Revision, "same-request");
                second = await StartAsync(generation1, authoring.Draft.Revision, "same-request");
                await RedriveAsync(generation1);

                Assert.Equal(first.ArtifactId, second.ArtifactId);
                Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
                var firstStatus = await StatusAsync(generation1, first.WorkflowExecutionId);
                var secondStatus = await StatusAsync(generation1, second.WorkflowExecutionId);
                if (firstStatus is null || secondStatus is null || firstStatus.Value.IsTerminal() || secondStatus.Value.IsTerminal())
                {
                    var firstStates = await generation1.GetRequiredService<IActivityExecutionStateStore>().ListAsync(first.WorkflowExecutionId);
                    var firstIncidents = await generation1.GetRequiredService<IIncidentStateStore>().ListAsync(first.WorkflowExecutionId);
                    var firstPoison = await generation1.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException(
                        $"Expected suspended runs. First={firstStatus} Second={secondStatus} States={JsonSerializer.Serialize(firstStates)} Incidents={JsonSerializer.Serialize(firstIncidents)} Poison={JsonSerializer.Serialize(firstPoison)}");
                }
                var firstBookmarks = await generation1.GetRequiredService<IBookmarkStateStore>().ListAsync(first.WorkflowExecutionId);
                if (firstBookmarks.Count == 0)
                {
                    var pending = await generation1.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(first.WorkflowExecutionId));
                    var poison = await generation1.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException($"No bookmark. Pending={JsonSerializer.Serialize(pending)} Poison={JsonSerializer.Serialize(poison)} Dispatch={first.CommandDispatchStatus}/{first.Reason}");
                }
                Assert.Single(firstBookmarks);
                Assert.Single(await generation1.GetRequiredService<IBookmarkStateStore>().ListAsync(second.WorkflowExecutionId));

                var executions = await generation1.GetRequiredService<IActivityExecutionStateStore>().ListAsync(first.WorkflowExecutionId);
                Assert.Equal(2, executions.Count);
                committedExecutionSequences = executions.ToDictionary(
                    state => state.Execution.ActivityExecutionId,
                    state => state.ExecutionSequence,
                    StringComparer.Ordinal);
                Assert.Single((await generation1.GetRequiredService<IDurableValueStateStore>().ListAsync(first.WorkflowExecutionId))
                    .Where(IsBoundaryInput));

                var executable = await generation1.GetRequiredService<IWorkflowExecutableStore>().FindAsync(first.ArtifactId);
                Assert.NotNull(executable);
                templateId = Assert.Single(await generation1.GetRequiredService<IExecutableActivityTemplateStore>().ListAsync()).TemplateId;
                Assert.Equal("sha256:test-run-suspending-template", executable!.CompatibilityMetadata["activity.templateHash"]);

                clock.Advance(ActivityDraftTestRunPublisher.DefaultRetention + TimeSpan.FromSeconds(1));
                var sweep = await generation1.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.False(sweep.DidWork);
                Assert.NotNull(await generation1.GetRequiredService<IWorkflowExecutableStore>().FindAsync(first.ArtifactId));
                Assert.NotNull(await generation1.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(templateId));
                Assert.NotNull(await generation1.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(first.SourceReferenceId));
            }
            finally
            {
                await DisposeStoreAsync(generation1Documents);
            }

            var generation2Documents = await OpenSqliteAsync(connectionString);
            try
            {
                await using var generation2 = BuildRuntimeOnlyProvider(generation2Documents, clock);
                await ResumeAsync(generation2, first.WorkflowExecutionId);
                await ResumeAsync(generation2, second.WorkflowExecutionId);
                var resumedFirstStatus = await StatusAsync(generation2, first.WorkflowExecutionId);
                if (resumedFirstStatus != WorkflowExecutionStatus.Completed)
                {
                    var poison = await generation2.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    var incidents = await generation2.GetRequiredService<IIncidentStateStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException($"Expected completed run after restart. Status={resumedFirstStatus} Poison={JsonSerializer.Serialize(poison)} Incidents={JsonSerializer.Serialize(incidents)}");
                }
                Assert.Equal(WorkflowExecutionStatus.Completed, await StatusAsync(generation2, second.WorkflowExecutionId));

                var resumedExecutions = await generation2.GetRequiredService<IActivityExecutionStateStore>().ListAsync(first.WorkflowExecutionId);
                Assert.Equal(committedExecutionSequences.Keys.Order(StringComparer.Ordinal),
                    resumedExecutions.Select(x => x.Execution.ActivityExecutionId).Order(StringComparer.Ordinal));
                Assert.All(resumedExecutions, state =>
                    Assert.Equal(committedExecutionSequences[state.Execution.ActivityExecutionId], state.ExecutionSequence));

                var durableValues = await generation2.GetRequiredService<IDurableValueStateStore>().ListAsync(first.WorkflowExecutionId);
                Assert.Single(durableValues.Where(IsBoundaryInput));
                var output = Assert.Single(durableValues.Where(IsBoundaryOutput));
                Assert.Equal("completed", output.InlineValue?.GetString());

                var outer = Assert.Single(resumedExecutions.Where(x => x.ParentActivityExecutionId is null));
                var hierarchy = await generation2.GetRequiredService<ActivityExecutionHierarchyReader>().ReadAsync(
                    first.WorkflowExecutionId,
                    outer.Execution.ActivityExecutionId,
                    null,
                    10,
                    "bookmarks,outcomes,incidents",
                    CancellationToken.None);
                Assert.NotNull(hierarchy);
                Assert.Single(hierarchy!.Items);
                Assert.Null(hierarchy.NextCursor);
                var layout = await generation2.GetRequiredService<ActivityExecutionLayoutReader>().ReadAsync(
                    first.WorkflowExecutionId,
                    outer.Execution.ActivityExecutionId,
                    CancellationToken.None);
                Assert.NotNull(layout);
                Assert.Equal("ExecutedReference", layout!.Selection);
                Assert.Equal(first.SourceReferenceId, layout.SourceReferenceId);

                var retainedSweep = await generation2.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.False(retainedSweep.DidWork);
                Assert.NotNull(await generation2.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(first.SourceReferenceId));

                var executionStore = generation2.GetRequiredService<IWorkflowExecutionStateStore>();
                Assert.True(await executionStore.DeleteAsync(first.WorkflowExecutionId));
                Assert.True(await executionStore.DeleteAsync(second.WorkflowExecutionId));
                var sweep = await generation2.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.Equal(3, sweep.DeletedReferenceCount);
                Assert.Equal(1, sweep.DeletedArtifactCount);
                Assert.Equal(1, sweep.DeletedActivityTemplateCount);
                Assert.Null(await generation2.GetRequiredService<IWorkflowExecutableStore>().FindAsync(first.ArtifactId));
                Assert.Null(await generation2.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(templateId));
                Assert.Null(await generation2.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(first.SourceReferenceId));
            }
            finally
            {
                await DisposeStoreAsync(generation2Documents);
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static async Task<ActivityDraftTestRunView> StartAsync(
        ServiceProvider provider,
        long revision,
        string correlationId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunPublisher>().StartAsync(new(
            AuthoringState.DraftId,
            revision,
            new Dictionary<string, ActivityDraftTestRunInput>
            {
                ["order"] = new("Present", JsonSerializer.SerializeToElement(new { id = "order-42" }))
            },
            correlationId));
    }

    private static async Task ResumeAsync(ServiceProvider provider, string workflowExecutionId)
    {
        var result = await provider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(
            workflowExecutionId,
            SuspendingActivity.StimulusType,
            SuspendingActivity.StimulusHash,
            requestedBy: "restart-test"));
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, result.Status);
        await RedriveAsync(provider);
    }

    private static async Task RedriveAsync(ServiceProvider provider)
    {
        var service = new RuntimeResumptionService(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>());
        await service.SweepAsync(new());
    }

    private static async Task<WorkflowExecutionStatus?> StatusAsync(ServiceProvider provider, string workflowExecutionId) =>
        (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId))?.Status;

    private static ServiceProvider BuildProvider(
        IDocumentStore documents,
        TimeProvider clock,
        AuthoringState authoring,
        IActivityPublishingAuthorizationContext? authorization = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IActivityDefinitionStore>(authoring.Definitions);
        services.AddSingleton<IActivityDefinitionDraftStore>(authoring.Drafts);
        services.AddSingleton<IActivityDefinitionLayoutStore>(authoring.Layouts);
        services.AddSingleton<IActivityDefinitionVersionPublicationStore, EmptyPublicationStore>();
        services.AddSingleton<IActivityTemplateCompiler, SuspendingTemplateCompiler>();
        services.AddSingleton<IActivityConstructor, SuspendingActivityConstructor>();
        if (authorization is not null)
            services.AddSingleton(authorization);
        services.AddSingleton<IActivityExecutionInspectionAuthorizationContext, AllowAllActivityExecutionInspectionAuthorizationContext>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.AddSingleton(documents);
        services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(documents));
        services.AddGroundworkRuntimeStores();

        var provider = services.BuildServiceProvider();
        RegisterConstructors(provider);
        return provider;
    }

    private static ServiceProvider BuildRuntimeOnlyProvider(IDocumentStore documents, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IActivityConstructor, SuspendingActivityConstructor>();
        services.AddSingleton<IActivityExecutionInspectionAuthorizationContext, AllowAllActivityExecutionInspectionAuthorizationContext>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton(documents);
        services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(documents));
        services.AddGroundworkRuntimeStores();

        var provider = services.BuildServiceProvider();
        RegisterConstructors(provider);
        return provider;
    }

    private static void RegisterConstructors(ServiceProvider provider)
    {
        var registry = provider.GetRequiredService<IActivityConstructorRegistry>();
        foreach (var constructor in provider.GetServices<IActivityConstructor>())
            registry.Add(constructor);
    }

    private static async Task<IDocumentStore> OpenSqliteAsync(string connectionString) =>
        await SqliteDocumentStoreFactory.CreateAsync(
            connectionString,
            ElsaRuntimeStorageManifest.Create(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            GroundworkTestAccess.DefaultScoped);

    private static async ValueTask DisposeStoreAsync(IDocumentStore store)
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (store is IDisposable disposable)
            disposable.Dispose();
    }

    private static bool IsBoundaryInput(DurableValueState value) =>
        value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) &&
        StringComparer.Ordinal.Equals(role, "input");

    private static bool IsBoundaryOutput(DurableValueState value) =>
        value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) &&
        StringComparer.Ordinal.Equals(role, "output");

    private static ExecutableActivityTemplate Template()
    {
        var root = new ExecutableNode(
            "root", "root", "test", "1", new("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        return new(
            "activity-template-test", "sha256:test", root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [new RuntimeRequirement("test", "1")], "test/1", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
    }

    private sealed class Sender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
    }

    private sealed class SuspendingTemplateCompiler : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(
            ActivityTemplateCompilerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = new ExecutableNode(
                "suspending-root",
                "suspending-root",
                "test.suspending-activity",
                "1",
                new(SuspendingActivityConstructor.ConsumerKeyValue, "1", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>());
            var descriptor = new GraphActivityDescriptor(
                request.Definition.Id,
                request.CandidateDefinitionVersionId,
                request.CandidateVersion,
                "sha256:test-run-suspending-template",
                child.ExecutableNodeId,
                null,
                [new("order", Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, null, "Order")],
                [],
                [new("result", Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, "Result")],
                [new("result", new("Literal", JsonSerializer.SerializeToElement("completed")))],
                ["order"],
                ["result"]);
            var root = new ExecutableNode(
                "graph-boundary",
                "graph-boundary",
                "test.graph-activity",
                request.CandidateVersion,
                new(
                    WellKnownRuntimeActivityConsumers.GraphActivity,
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string> { ["graph.templateHash"] = descriptor.TemplateHash },
                [new ExecutableChildSlot("Graph.Entry", [child])]);
            var targets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [SuspendingActivity.ResumeTargetId] = new(
                    SuspendingActivity.ResumeTargetId,
                    child.ExecutableNodeId,
                    nameof(SuspendingActivity.Resume),
                    new Dictionary<string, string>())
            };
            var template = new ExecutableActivityTemplate(
                "activity-template-test-run-suspending",
                "sha256:test-run-suspending-template",
                root,
                targets,
                [],
                [],
                [
                    new(WellKnownRuntimeActivityConsumers.GraphActivity, RuntimeActivityDescriptor.InitialSchemaVersion),
                    new(SuspendingActivityConstructor.ConsumerKeyValue, "1")
                ],
                "test-provider/1",
                new Dictionary<string, string>(),
                DateTimeOffset.UnixEpoch);
            return ValueTask.FromResult(new ActivityTemplateCompilerResult(
                template,
                new(2, 1, 0, 2, 1, 0, 0),
                [],
                []));
        }

        private static JsonElement Type() => JsonSerializer.SerializeToElement(new { alias = "object" });
    }

    private sealed record SuspendingDescriptor;

    private sealed class SuspendingActivityConstructor : IActivityConstructor<SuspendingDescriptor>
    {
        public const string ConsumerKeyValue = "test.suspending-activity";
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            SuspendingDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) => new(new SuspendingActivity());
    }

    private sealed class SuspendingActivity : CodeActivity
    {
        public const string ResumeTargetId = "resume-target:test-run";
        public const string StimulusType = "activity-test-run-signal";
        public const string StimulusHash = "activity-test-run-signal:ready";

        protected override void Execute(IActivityExecutionContext context) =>
            context.CreateBookmark(new("bookmark:test-run", ResumeTargetId, StimulusType, StimulusHash));

        [ResumeTarget(ResumeTargetId)]
        public void Resume()
        {
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _value;
        public string Generate() => $"id-{Interlocked.Increment(ref _value)}";
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class AuthoringState
    {
        public const string DraftId = "draft-test-run";

        private AuthoringState(
            ActivityDefinition definition,
            ActivityDefinitionDraft draft,
            ActivityDefinitionDraftLayout layout)
        {
            Draft = draft;
            Definitions = new DefinitionStore(definition);
            Drafts = new DraftStore(draft);
            Layouts = new LayoutStore(layout);
        }

        public ActivityDefinitionDraft Draft { get; }
        public DefinitionStore Definitions { get; }
        public DraftStore Drafts { get; }
        public LayoutStore Layouts { get; }

        public static AuthoringState Create(string? tenantId = null)
        {
            var definition = new ActivityDefinition
            {
                Id = "definition-test-run",
                ActivityTypeKey = "test.suspending",
                Category = "Tests",
                DisplayName = "Suspending test activity",
                TenantId = tenantId
            };
            var draft = new ActivityDefinitionDraft
            {
                Id = DraftId,
                DefinitionId = definition.Id,
                Revision = 4,
                Status = ActivityDefinitionDraftStatus.Active,
                TenantId = tenantId,
                State = new(
                    new(
                        "1",
                        [new("order", "Order", new("object"), true, null, WellKnownRuntimeDurableValueStorageDrivers.Json)],
                        [new("result", "Result", new("object"), true, WellKnownRuntimeDurableValueStorageDrivers.Json)],
                        [new("done", "Done", true)]),
                    new("test.provider", "1", JsonSerializer.SerializeToElement(new { })),
                    new Dictionary<string, string>())
            };
            var layout = new ActivityDefinitionDraftLayout
            {
                Id = "layout-test-run",
                DraftId = draft.Id,
                Revision = draft.Revision,
                TenantId = tenantId,
                Records = []
            };
            return new(definition, draft, layout);
        }
    }

    private sealed class DefinitionStore(ActivityDefinition definition) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(StringComparer.Ordinal.Equals(id, definition.Id) ? definition : throw new KeyNotFoundException(id));
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinition>>([definition]);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DraftStore(ActivityDefinitionDraft draft) : IActivityDefinitionDraftStore
    {
        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionDraft?>(StringComparer.Ordinal.Equals(draftId, draft.Id) ? draft : null);
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([draft]);
    }

    private sealed class LayoutStore(ActivityDefinitionDraftLayout layout) : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraftLayout?>(layout);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionLayout?>(null);
    }

    private sealed class EmptyPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionPublication?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }
}
