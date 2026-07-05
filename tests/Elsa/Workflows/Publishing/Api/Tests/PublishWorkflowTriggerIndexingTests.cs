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

        await Assert.ThrowsAsync<WorkflowTriggerExtractionException>(() =>
            handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Empty(await _bindingStore.ListByArtifactAsync((await _executableStore.ListAsync()).SingleOrDefault()?.Identity.ArtifactId ?? "none"));
    }

    private PublishWorkflowRequestHandler Handler(params IActivityTriggerStimulusProvider[] providers)
    {
        var workflowVersion = WorkflowVersion(TriggerNode("trigger-node"));
        var triggerActivity = TriggerActivityVersion();
        return new PublishWorkflowRequestHandler(
            TestCompiler.Create(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore([triggerActivity]),
                BuildStructureService(),
                TestWellKnownTypeRegistry.Create()),
            _executableStore,
            new WorkflowTriggerIndexer(new WorkflowTriggerBindingExtractor(providers), _bindingStore));
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

    private static ActivityDefinitionVersion TriggerActivityVersion() =>
        new("1.0.0", "activity-definition-1")
        {
            Id = "activity-trigger",
            ExecutionType = ActivityExecutionType.Trigger,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = TriggerActivityTypeKey,
                Category = "Test"
            },
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = [new InputDefinition("EventName", "EventName", new TypeReference("String"), null, "EventName", null)]
        };

    private sealed class StubTriggerProvider(string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public TriggerStimulusDescriptor? Describe(ExecutableNode node) =>
            node.ActivityType == TriggerActivityTypeKey
                ? new TriggerStimulusDescriptor(stimulusType, stimulusHash)
                : null;
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
