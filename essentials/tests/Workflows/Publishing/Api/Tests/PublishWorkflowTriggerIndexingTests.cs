using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
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
    private readonly InMemoryWorkflowActivationAuthority _activationAuthority = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();
    private readonly InMemoryPublicationPolicyStore _policyStore = new();
    private readonly InMemoryPublicationProjectionIntentStore _intentStore = new();

    [Fact]
    public async Task PublishingStartTrigger_IndexesBindingOverPublishedArtifact()
    {
        var view = await Handler(new StubTriggerProvider("Event", "hash-order")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var byStimulus = (await _bindingStore.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "hash-order"))).Items;
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

        Assert.Empty((await _bindingStore.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "hash-old"))).Items);
        Assert.Equal(
            view.ArtifactId,
            Assert.Single((await _bindingStore.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery("Event", "hash-new"))).Items).ArtifactId);
    }

    [Fact]
    public async Task PublishingAfterACrashMidReplacement_ServesOnlyTheNewPublication()
    {
        await Handler("old", new StubTriggerProvider("Event", "hash-old")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        // The replacing publish stops for good once its switch commits, before its journal is written. The slot and its
        // bindings switched in that one commit (#2230), so the interrupted publication serves and the old one does not.
        var extractor = new WorkflowTriggerBindingExtractor([new StubTriggerProvider("Event", "hash-interrupted")]);
        PauseAfterSwitch? stopping = null;
        var interrupted = Handler(
                WorkflowVersion(TriggerNode("trigger-node", [Input("EventName", "interrupted")])),
                TriggerActivityVersion(),
                extractor,
                new WorkflowTriggerIndexer(extractor, _bindingStore),
                wrapSwitch: inner => stopping = new PauseAfterSwitch(inner))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        Assert.Same(stopping!.Paused, await Task.WhenAny(interrupted, stopping.Paused));
        Assert.Empty(await ServingAsync("hash-old"));
        Assert.Single(await ServingAsync("hash-interrupted"));

        var view = await Handler("new", new StubTriggerProvider("Event", "hash-new")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.Equal(view.ArtifactId, Assert.Single(await ServingAsync("hash-new")).ArtifactId);
        Assert.Empty(await ServingAsync("hash-old"));
        Assert.Empty(await ServingAsync("hash-interrupted"));
    }

    [Fact]
    public async Task UnroutableTrigger_FailsThePublish_AndWritesNoBinding()
    {
        // No provider recognizes the trigger node, so its stimulus cannot be derived.
        var handler = Handler();

        await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() =>
            handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Empty(await _bindingStore.ListAllByArtifactAsync((await _executableStore.ListAllAsync()).SingleOrDefault()?.Identity.ArtifactId ?? "none"));
    }

    public static TheoryData<IActivityTriggerStimulusProvider, string, string> SecretReadBackstops => new()
    {
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.Path) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.SupportedMethods) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.CanStartWorkflow) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.Authorize) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.Policy) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.RequestTimeout) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.RequestSizeLimit) },
        { new HttpEndpointTriggerStimulusProvider(), HttpEndpoint.ActivityType, nameof(HttpEndpoint.ResponseMode) },
        { new CronTriggerStimulusProvider(), Cron.ActivityType, nameof(Cron.Expression) },
        { new TimerTriggerStimulusProvider(), Timer.ActivityType, nameof(Timer.Interval) }
    };

    /// <summary>
    /// Publication refuses a secret reference on these inputs before any reader runs (their activities declare them);
    /// this backstop keeps an artifact that skipped publication from reading a secret as unauthored or as a generic
    /// non-literal.
    /// </summary>
    [Theory]
    [MemberData(nameof(SecretReadBackstops))]
    public void A_publish_time_literal_reader_refuses_a_secret_read_with_the_fixed_code(
        IActivityTriggerStimulusProvider provider,
        string activityType,
        string inputName)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.CanStartWorkflow)] = LiteralBinding(nameof(HttpEndpoint.CanStartWorkflow), "Boolean", true),
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "String", "orders")
        };
        bindings[inputName] = SecretBindingCompilerFixture.SecretReadBinding(inputName);
        var node = new ExecutableNode(
            executableNodeId: "trigger-node",
            authoredActivityId: "trigger-node",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: bindings,
            metadata: new Dictionary<string, string>());

        SecretBindingCompilerFixture.AssertReaderRefusesSecretRead(() => provider.Describe(node), "trigger-node", inputName);
    }

    private static RuntimeInputBinding LiteralBinding(string name, string typeAlias, object value)
    {
        var type = new ValueTypeDescriptor(typeAlias);
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private async Task<IReadOnlyCollection<WorkflowTriggerBinding>> ServingAsync(string stimulusHash) =>
        (await _bindingStore.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", stimulusHash))).Items;

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

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        ActivityDefinitionVersion triggerActivity,
        IWorkflowTriggerBindingExtractor extractor,
        IWorkflowTriggerIndexer indexer,
        Type? clrType = null,
        Func<IWorkflowActivationSwitch, IWorkflowActivationSwitch>? wrapSwitch = null)
    {
        var activationSwitch = ActivationSwitchDecoration.Create(_activationAuthority, _referenceStore, TimeProvider.System, _bindingStore);
        var coordinator = new WorkflowActivationCoordinator(
            _activationAuthority,
            wrapSwitch?.Invoke(activationSwitch) ?? activationSwitch,
            _referenceStore,
            TestRootWriteLeases.Create(_executableStore),
            TimeProvider.System,
            indexer,
            _bindingStore);
        var activator = new PublicationActivator(coordinator, _publicationStore, _activationAuthority, _referenceStore, TimeProvider.System);
        return new PublishWorkflowRequestHandler(
            Compiler(workflowVersion, triggerActivity, clrType),
            _executableStore,
            _referenceStore,
            extractor,
            _bindingStore,
            new NullLayoutStore(),
            _activationAuthority,
            _policyStore,
            new PublicationPolicyResolver(),
            _publicationStore,
            new PublicationPreflightService(),
            activator,
            TimeProvider.System,
            workflowVersionStore: new FakeVersionStore(workflowVersion),
            expressionValidator: AlwaysValidExpressionValidator.Instance);
    }

    private static WorkflowExecutableCompiler Compiler(
        WorkflowDefinitionVersion workflowVersion,
        ActivityDefinitionVersion triggerActivity,
        Type? clrType)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(TestTriggerActivity), TypeAliasConvention.CanonicalAlias(typeof(TestTriggerActivity)));
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

    private sealed class AlwaysValidExpressionValidator : IExpressionDraftSemanticValidator
    {
        public static readonly AlwaysValidExpressionValidator Instance = new();

        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState workflow,
            string workflowDefinitionVersionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExpressionDraftValidationResult(ExpressionDraftValidationState.Valid, []));
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
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(clrType ?? typeof(TestTriggerActivity))),
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

    private static IActivityStructureService BuildStructureService() => new DefaultActivityStructureService([]);

    // Minimal registered trigger CLR type: an EventName input plus the typed-result marker, so compiled
    // generic trigger nodes pin an activity contract at publish time (VF-ACT-001 gate).
    private sealed class TestTriggerActivity : IActivityResult<ActivityUnit>
    {
        [ActivityInput(Key = "EventName")]
        public string? EventName { get; set; }
    }
}
