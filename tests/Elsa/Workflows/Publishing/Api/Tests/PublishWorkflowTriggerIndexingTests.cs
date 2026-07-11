using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
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
        await Handler(new StubTriggerProvider("Event", "hash-old")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var view = await Handler(new StubTriggerProvider("Event", "hash-new")).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

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
    [InlineData("Elsa.Event", "Event", 1)]
    [InlineData("Elsa.Timer", "Timer", 1)]
    [InlineData("Elsa.Cron", "Cron", 1)]
    [InlineData("Elsa.HttpEndpoint", "HttpEndpoint", 2)]
    public async Task FirstPartyTriggerMatrix_ValidPublicationIndexesEveryCompleteBinding(
        string activityType,
        string stimulusType,
        int expectedBindingCount)
    {
        var provider = MatrixProvider.Valid(activityType, stimulusType, expectedBindingCount);

        var view = await Handler(activityType, provider).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var bindings = await _bindingStore.ListByArtifactAsync(view.ArtifactId);
        Assert.Equal(expectedBindingCount, bindings.Count);
        Assert.Equal(provider.Descriptors.Select(x => x.StimulusHash).Order(), bindings.Select(x => x.StimulusHash).Order());
        Assert.All(bindings, binding =>
        {
            Assert.Equal(stimulusType, binding.StimulusType);
            Assert.Equal("trigger-node", binding.ExecutableNodeId);
            Assert.False(string.IsNullOrWhiteSpace(binding.TriggerBindingId));
        });
    }

    [Theory]
    [InlineData("Elsa.Event", "Event")]
    [InlineData("Elsa.Timer", "Timer")]
    [InlineData("Elsa.Cron", "Cron")]
    [InlineData("Elsa.HttpEndpoint", "HttpEndpoint")]
    public async Task FirstPartyTriggerMatrix_InvalidPublicationPreservesSeededRegistration(
        string activityType,
        string stimulusType)
    {
        var seededView = await Handler(activityType, MatrixProvider.Valid(activityType, stimulusType, 1))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var seeded = Assert.Single(await _bindingStore.ListByArtifactAsync(seededView.ArtifactId));

        await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() =>
            Handler(activityType, new InvalidMatrixProvider(activityType, stimulusType))
                .Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        var preserved = Assert.Single(await _bindingStore.ListByArtifactAsync(seededView.ArtifactId));
        Assert.Equal(seeded, preserved);
    }

    private PublishWorkflowRequestHandler Handler(params IActivityTriggerStimulusProvider[] providers)
        => Handler(TriggerActivityTypeKey, providers);

    private PublishWorkflowRequestHandler Handler(string activityType, params IActivityTriggerStimulusProvider[] providers)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node"));
        var triggerActivity = TriggerActivityVersion(activityType);
        return new PublishWorkflowRequestHandler(
            TestCompiler.Create(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore([triggerActivity]),
                BuildStructureService(),
                TestWellKnownTypeRegistry.Create()),
            _executableStore,
            _referenceStore,
            new WorkflowTriggerIndexer(new WorkflowTriggerBindingExtractor(providers), _bindingStore),
            new NullLayoutStore());
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
            State = new WorkflowDefinitionState([], rootActivity, [], [], null, null)
        };

    private static ActivityNode TriggerNode(string nodeId) =>
        new(nodeId, "activity-trigger", Inputs: [new WorkflowArgumentState("EventName", new ArgumentValue("order-shipped", "Literal"), null, null, null, null)], Outputs: [], Structure: null);

    private static ActivityDefinitionVersion TriggerActivityVersion(string activityType = TriggerActivityTypeKey) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = "activity-trigger",
            ExecutionType = ActivityExecutionType.Trigger,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityType,
                Category = "Test"
            },
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = [new InputDefinition("EventName", "EventName", new TypeReference("String"), null, "EventName", null)]
        };

    private sealed class StubTriggerProvider(string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            node.ActivityType == TriggerActivityTypeKey
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(stimulusType, stimulusHash)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class MatrixProvider : IActivityTriggerStimulusProvider
    {
        private MatrixProvider(string activityType, string stimulusType, IReadOnlyCollection<TriggerStimulusDescriptor> descriptors)
        {
            ActivityType = activityType;
            StimulusType = stimulusType;
            Descriptors = descriptors;
        }

        private string ActivityType { get; }
        private string StimulusType { get; }
        public IReadOnlyCollection<TriggerStimulusDescriptor> Descriptors { get; }
        public string ProviderId => $"first-party.{StimulusType.ToLowerInvariant()}";

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            node.ActivityType == ActivityType
                ? ActivityTriggerStimulusResult.Recognized(Descriptors)
                : ActivityTriggerStimulusResult.NotRecognized;

        public static MatrixProvider Valid(string activityType, string stimulusType, int bindingCount) =>
            new(
                activityType,
                stimulusType,
                Enumerable.Range(1, bindingCount)
                    .Select(index => new TriggerStimulusDescriptor(stimulusType, $"sha256:{stimulusType.ToLowerInvariant()}:{index}"))
                    .ToArray());
    }

    private sealed class InvalidMatrixProvider(string activityType, string stimulusType) : IActivityTriggerStimulusProvider
    {
        public string ProviderId => $"first-party.{stimulusType.ToLowerInvariant()}";

        public ActivityTriggerStimulusResult Describe(ExecutableNode node)
        {
            if (node.ActivityType != activityType)
                return ActivityTriggerStimulusResult.NotRecognized;

            throw new ArgumentException($"Invalid {stimulusType} routing input.");
        }
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
