using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cron = Elsa.Activities.Scheduling.Activities.Cron;
using Event = Elsa.Activities.Primitives.Activities.Event;
using Timer = Elsa.Activities.Scheduling.Activities.Timer;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

// E3-1 acceptance at publish level: publishing a workflow whose root is a start-trigger indexes a durable
// trigger binding over the pinned artifact (so a later stimulus can start a fresh instance), and an
// unroutable trigger FAILS THE PUBLISH rather than persisting a trigger that can never fire.
public sealed class PublishWorkflowTriggerIndexingTests
{
    private const string TriggerActivityTypeKey = "Test.Trigger";

    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();
    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();
    private readonly InMemoryRecurringTriggerScheduleStore _scheduleStore = new();
    private readonly InMemoryPublicationSlotStore _slotStore = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();
    private readonly InMemoryPublicationPolicyStore _policyStore = new();
    private readonly InMemoryPublicationProjectionIntentStore _intentStore = new();

    [Fact]
    public async Task PublishingStartTrigger_IndexesBindingOverPublishedArtifact()
    {
        var view = await Handler(new StubTriggerProvider("Event", "hash-order")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var byStimulus = await _bindingStore.ListByStimulusAsync("Event", "hash-order");
        var binding = Assert.Single(byStimulus);
        Assert.Equal(view.ArtifactId, binding.ArtifactId);
        Assert.Equal("trigger-node", binding.ExecutableNodeId);
        Assert.Equal(view.ArtifactHash, binding.ArtifactHash);
    }

    [Fact]
    public async Task RepublishingReplacesTriggerBindings()
    {
        await Handler("old", new StubTriggerProvider("Event", "hash-old")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var view = await Handler("new", new StubTriggerProvider("Event", "hash-new")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.Empty(await _bindingStore.ListByStimulusAsync("Event", "hash-old"));
        Assert.Equal(view.ArtifactId, Assert.Single(await _bindingStore.ListByStimulusAsync("Event", "hash-new")).ArtifactId);
    }

    [Fact]
    public async Task UnroutableTrigger_FailsThePublish_AndWritesNoBinding()
    {
        // No provider recognizes the trigger node, so its stimulus cannot be derived.
        var handler = Handler();

        await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() =>
            handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Empty(await _bindingStore.ListByArtifactAsync((await _executableStore.ListAsync()).SingleOrDefault()?.Identity.ArtifactId ?? "none"));
    }

    [Theory]
    [MemberData(nameof(FirstPartyScenarios))]
    public async Task FirstPartyTriggerMatrix_ValidPublicationIndexesEveryCompleteBinding(
        FirstPartyScenario scenario)
    {
        var view = await FirstPartyHandler(scenario, scenario.ValidInputs)
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var bindings = await _bindingStore.ListByArtifactAsync(view.ArtifactId);
        Assert.Equal(scenario.ExpectedBindingCount, bindings.Count);
        Assert.All(bindings, binding =>
        {
            Assert.Equal(scenario.StimulusType, binding.StimulusType);
            Assert.Equal("trigger-node", binding.ExecutableNodeId);
            Assert.False(string.IsNullOrWhiteSpace(binding.TriggerBindingId));
            Assert.False(string.IsNullOrWhiteSpace(binding.StimulusHash));
        });

        var publicationId = Assert.Single(bindings.Select(binding => binding.PublicationId).Distinct())!;
        var schedule = (await _scheduleStore.ListByPublicationAsync(publicationId)).SingleOrDefault();
        if (scenario.IsRecurring)
        {
            Assert.NotNull(schedule);
            Assert.Equal(scenario.StimulusType, schedule.StimulusType);
            Assert.Equal(Assert.Single(bindings).StimulusHash, schedule.StimulusHash);
        }
        else
            Assert.Null(schedule);
    }

    [Theory]
    [MemberData(nameof(FirstPartyScenarios))]
    public async Task FirstPartyTriggerMatrix_InvalidPublicationPreservesSeededRegistration(
        FirstPartyScenario scenario)
    {
        var seededView = await FirstPartyHandler(scenario, scenario.ValidInputs)
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var seededBindings = await _bindingStore.ListByArtifactAsync(seededView.ArtifactId);
        var seededPublicationId = Assert.Single(seededBindings.Select(binding => binding.PublicationId).Distinct())!;
        var seededSchedule = (await _scheduleStore.ListByPublicationAsync(seededPublicationId)).SingleOrDefault();

        await AssertInvalidPublicationAsync(scenario, () =>
            FirstPartyHandler(scenario, scenario.InvalidInputs)
                .Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Equal(
            seededBindings.OrderBy(x => x.TriggerBindingId),
            (await _bindingStore.ListByArtifactAsync(seededView.ArtifactId)).OrderBy(x => x.TriggerBindingId));
        Assert.Equal(
            seededSchedule,
            (await _scheduleStore.ListByPublicationAsync(seededPublicationId)).SingleOrDefault());
    }

    [Theory]
    [MemberData(nameof(FirstPartyScenarios))]
    public async Task LegacyActionCatalogRow_RepublishProjectsTriggerAndPreservesPriorRegistrationOnFailure(
        FirstPartyScenario scenario)
    {
        var seededView = await FirstPartyHandler(scenario, scenario.ValidInputs, legacyActionCatalog: true)
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        Assert.Equal(scenario.ExpectedBindingCount, (await _bindingStore.ListByArtifactAsync(seededView.ArtifactId)).Count);

        var invalidExecutable = await FirstPartyCompiler(scenario, scenario.InvalidInputs, legacyActionCatalog: true)
            .CompileAsync(new WorkflowExecutableCompileRequest(
                "version-1",
                WorkflowExecutableReferenceScope.Published,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                null,
                "artifact-"));
        var seededBinding = new WorkflowTriggerBinding(
            WorkflowTriggerBinding.BuildId(invalidExecutable.Identity.ArtifactId, "trigger-node", "seeded-stimulus"),
            invalidExecutable.Identity.ArtifactId,
            invalidExecutable.Identity.DefinitionId,
            invalidExecutable.Identity.ArtifactVersion,
            invalidExecutable.Identity.ArtifactHash,
            "trigger-node",
            scenario.StimulusType,
            "seeded-stimulus",
            null,
            new Dictionary<string, string> { ["seed"] = "legacy" },
            DateTimeOffset.UnixEpoch);
        await _bindingStore.SaveAsync(seededBinding);
        RecurringTriggerSchedule? seededSchedule = null;
        if (scenario.IsRecurring)
        {
            seededSchedule = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildId(invalidExecutable.Identity.ArtifactId, "trigger-node"),
                invalidExecutable.Identity.ArtifactId,
                scenario.StimulusType,
                "seeded-stimulus",
                RecurringScheduleKind.Interval,
                "PT1H",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            await _scheduleStore.SaveAsync(seededSchedule);
        }

        await AssertInvalidPublicationAsync(scenario, () =>
            FirstPartyHandler(scenario, scenario.InvalidInputs, legacyActionCatalog: true)
                .Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Equal(seededBinding, Assert.Single(await _bindingStore.ListByArtifactAsync(invalidExecutable.Identity.ArtifactId)));
        Assert.Equal(
            seededSchedule,
            await _scheduleStore.FindAsync(RecurringTriggerSchedule.BuildId(invalidExecutable.Identity.ArtifactId, "trigger-node")));
    }

    private static async Task AssertInvalidPublicationAsync(FirstPartyScenario scenario, Func<Task> publish)
    {
        if (scenario.IsRecurring)
        {
            var exception = await Assert.ThrowsAsync<PublicationActivationException>(publish);
            Assert.Equal("projection_preparation_failed", exception.Code);
            return;
        }

        var preflightException = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(publish);
        Assert.Equal([scenario.ActivityType], preflightException.ProviderIds);
    }

    public static TheoryData<FirstPartyScenario> FirstPartyScenarios =>
    [
        new(
            Event.ActivityType,
            "Event",
            1,
            false,
            [Input(nameof(Event.EventName), "order-shipped")],
            [Input(nameof(Event.EventName), "")],
            [Definition(nameof(Event.EventName), "String")],
            new EventTriggerStimulusProvider(),
            [],
            typeof(Event)),
        new(
            Timer.ActivityType,
            "Timer",
            1,
            true,
            [Input(nameof(Timer.Interval), "PT5M")],
            [Input(nameof(Timer.Interval), "not-an-interval")],
            [Definition(nameof(Timer.Interval), "String")],
            new TimerTriggerStimulusProvider(),
            [new TimerRecurringScheduleProvider()],
            typeof(Timer)),
        new(
            Cron.ActivityType,
            "Cron",
            1,
            true,
            [Input(nameof(Cron.Expression), "0 * * * *")],
            [Input(nameof(Cron.Expression), "not-a-cron")],
            [Definition(nameof(Cron.Expression), "String")],
            new CronTriggerStimulusProvider(),
            [new CronRecurringScheduleProvider()],
            typeof(Cron)),
        new(
            HttpEndpoint.ActivityType,
            "HttpEndpoint",
            2,
            false,
            [
                Input(nameof(HttpEndpoint.Path), "orders/{id}"),
                ObjectInput(nameof(HttpEndpoint.SupportedMethods), new[] { "GET", "POST" }),
                ObjectInput(nameof(HttpEndpoint.CanStartWorkflow), true)
            ],
            [
                Input(nameof(HttpEndpoint.Path), ""),
                ObjectInput(nameof(HttpEndpoint.SupportedMethods), new[] { "GET", "POST" }),
                ObjectInput(nameof(HttpEndpoint.CanStartWorkflow), true)
            ],
            [
                Definition(nameof(HttpEndpoint.Path), "String"),
                Definition(nameof(HttpEndpoint.SupportedMethods), "Object"),
                Definition(nameof(HttpEndpoint.CanStartWorkflow), "Boolean")
            ],
            new HttpEndpointTriggerStimulusProvider(),
            [],
            typeof(HttpEndpoint))
    ];

    private PublishWorkflowRequestHandler Handler(params IActivityTriggerStimulusProvider[] providers)
        => Handler(TriggerActivityTypeKey, providers);

    private PublishWorkflowRequestHandler Handler(string eventName, IActivityTriggerStimulusProvider provider)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node", [Input("EventName", eventName)]));
        var triggerActivity = TriggerActivityVersion();
        var extractor = new WorkflowTriggerBindingExtractor([provider]);
        return Handler(workflowVersion, triggerActivity, extractor, new WorkflowTriggerIndexer(extractor, _bindingStore));
    }

    private PublishWorkflowRequestHandler Handler(string activityType, params IActivityTriggerStimulusProvider[] providers)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node"));
        var triggerActivity = TriggerActivityVersion(activityType);
        var extractor = new WorkflowTriggerBindingExtractor(providers);
        return Handler(workflowVersion, triggerActivity, extractor, new WorkflowTriggerIndexer(extractor, _bindingStore));
    }

    private PublishWorkflowRequestHandler FirstPartyHandler(
        FirstPartyScenario scenario,
        IReadOnlyCollection<WorkflowArgumentState> inputs,
        bool legacyActionCatalog = false)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node", inputs));
        var triggerActivity = FirstPartyActivityVersion(scenario, legacyActionCatalog);
        var extractor = new WorkflowTriggerBindingExtractor([scenario.TriggerProvider]);
        IWorkflowTriggerIndexer indexer = new WorkflowTriggerIndexer(extractor, _bindingStore);
        indexer = new RecurringTriggerScheduleIndexer(
            indexer,
            scenario.ScheduleProviders,
            _scheduleStore,
            new RecurringScheduleCalculator(),
            TimeProvider.System,
            NullLogger<RecurringTriggerScheduleIndexer>.Instance);
        return Handler(workflowVersion, triggerActivity, extractor, indexer, scenario.ClrType);
    }

    private WorkflowExecutableCompiler FirstPartyCompiler(
        FirstPartyScenario scenario,
        IReadOnlyCollection<WorkflowArgumentState> inputs,
        bool legacyActionCatalog)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node", inputs));
        var triggerActivity = FirstPartyActivityVersion(scenario, legacyActionCatalog);
        return Compiler(workflowVersion, triggerActivity, scenario.ClrType);
    }

    private static ActivityDefinitionVersion FirstPartyActivityVersion(FirstPartyScenario scenario, bool legacyActionCatalog) =>
        TriggerActivityVersion(
            scenario.ActivityType,
            scenario.InputDefinitions,
            legacyActionCatalog ? ActivityExecutionType.Action : ActivityExecutionType.Trigger,
            legacyActionCatalog ? scenario.ClrType : null);

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        ActivityDefinitionVersion triggerActivity,
        IWorkflowTriggerBindingExtractor extractor,
        IWorkflowTriggerIndexer indexer,
        Type? clrType = null)
    {
        var reconciler = new PublicationProjectionReconciler(
            _intentStore,
            _executableStore,
            indexer,
            _bindingStore,
            TimeProvider.System,
            _scheduleStore);
        var activator = new PublicationActivator(_slotStore, _publicationStore, reconciler, TimeProvider.System);
        return new PublishWorkflowRequestHandler(
            Compiler(workflowVersion, triggerActivity, clrType),
            _executableStore,
            _referenceStore,
            extractor,
            _bindingStore,
            new NullLayoutStore(),
            TestRootWriteLeases.Create(_executableStore),
            _policyStore,
            new PublicationPolicyResolver(),
            _slotStore,
            _publicationStore,
            new PublicationPreflightService(),
            activator,
            TimeProvider.System);
    }

    private static WorkflowExecutableCompiler Compiler(
        WorkflowDefinitionVersion workflowVersion,
        ActivityDefinitionVersion triggerActivity,
        Type? clrType)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        if (clrType is not null)
            registry.RegisterType(clrType, TypeAliasConvention.CanonicalAlias(clrType));

        return TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([triggerActivity]),
            BuildStructureService(),
            registry);
    }

    private sealed class NullLayoutStore : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
        };

    private static ActivityNode TriggerNode(string nodeId) =>
        TriggerNode(nodeId, [Input("EventName", "order-shipped")]);

    private static ActivityNode TriggerNode(string nodeId, IReadOnlyCollection<WorkflowArgumentState> inputs) =>
        new(nodeId, "activity-trigger", Inputs: inputs, Outputs: [], Structure: null);

    private static ActivityDefinitionVersion TriggerActivityVersion(
        string activityType = TriggerActivityTypeKey,
        IReadOnlyCollection<InputDefinition>? inputs = null,
        ActivityExecutionType executionType = ActivityExecutionType.Trigger,
        Type? clrType = null) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = "activity-trigger",
            ExecutionType = executionType,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityType,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(
                new ClrActivityDescriptor(clrType is null ? "Object" : TypeAliasConvention.CanonicalAlias(clrType)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Inputs = inputs ?? [Definition("EventName", "String")]
        };

    private static WorkflowArgumentState Input(string name, string value) =>
        new(name, new ArgumentValue(value, "Literal"), null, null, null, null);

    private static WorkflowArgumentState ObjectInput(string name, object value) =>
        new(name, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Object"), null, null, null, null);

    private static InputDefinition Definition(string name, string type) =>
        new(name, name, new TypeReference(type), null, name, null, false);

    private sealed class StubTriggerProvider(string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            node.ActivityType == TriggerActivityTypeKey
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(stimulusType, stimulusHash)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    public sealed record FirstPartyScenario(
        string ActivityType,
        string StimulusType,
        int ExpectedBindingCount,
        bool IsRecurring,
        IReadOnlyCollection<WorkflowArgumentState> ValidInputs,
        IReadOnlyCollection<WorkflowArgumentState> InvalidInputs,
        IReadOnlyCollection<InputDefinition> InputDefinitions,
        IActivityTriggerStimulusProvider TriggerProvider,
        IReadOnlyCollection<IRecurringTriggerScheduleProvider> ScheduleProviders,
        Type ClrType)
    {
        public override string ToString() => ActivityType;
    }

    private static IActivityStructureService BuildStructureService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<IActivityStructureService, DefaultActivityStructureService>(services);
        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IActivityStructureService>(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId ? Task.FromResult(version) : throw new ArgumentException($"missing '{versionId}'");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
