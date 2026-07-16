using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Design.Services;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowDesignTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Options_include_only_visible_definitions_with_one_live_published_source()
    {
        var definitions = new StubDefinitionStore(
            Definition("visible", "Visible"),
            Definition("missing", "Missing"),
            Definition("ambiguous", "Ambiguous"),
            Definition("test-run", "Test run"),
            Definition("retired", "Retired"));
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Source("visible-ref", "visible", "visible-artifact"));
        await references.SaveAsync(Source("ambiguous-a", "ambiguous", "ambiguous-a-artifact"));
        await references.SaveAsync(Source("ambiguous-b", "ambiguous", "ambiguous-b-artifact"));
        await references.SaveAsync(Source("test-ref", "test-run", "test-artifact", WorkflowExecutableReferenceScope.TestRun));
        await references.SaveAsync(Source("retired-ref", "retired", "retired-artifact") with { DeletedAt = Now.AddMinutes(-1) });
        var provider = new WorkflowDefinitionOptionsProvider(definitions, references, new FixedTimeProvider(Now));

        var options = await provider.GetOptionsAsync(null!);

        var option = Assert.Single(options);
        Assert.Equal("Visible", option.Label);
        Assert.Equal("visible", option.Value.GetString());
        Assert.Equal(DispatchWorkflowConstants.WorkflowDefinitionOptionsKey, provider.Key);
    }

    [Fact]
    public async Task Pin_contribution_carries_exact_executable_and_source_identity()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        // Content-addressed executables may retain source facts from the first definition that produced the
        // shared behavior. The selected source reference, not those deduplicated identity facts, owns target provenance.
        var executable = Executable("child-artifact", "first-content-owner", node);
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var source = Source("published-source", "child-definition", executable.Identity.ArtifactId);
        await executables.SaveAsync(executable);
        await references.SaveAsync(source);
        var sourceProvider = new DispatchPinSource(references, executables, new FixedTimeProvider(Now));

        var contributions = await sourceProvider.GetMetadataAsync(Context(node));

        var contribution = Assert.Single(contributions);
        Assert.Equal(node.ExecutableNodeId, contribution.ExecutableNodeId);
        Assert.Equal(DispatchWorkflowConstants.PinnedTargetMetadataKey, contribution.Key);
        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(contribution.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(pin);
        Assert.Equal(executable.Identity, pin!.Executable);
        Assert.Equal(source.SourceReferenceId, pin.Source.SourceReferenceId);
        Assert.Equal(source.SourceKind, pin.Source.SourceKind);
        Assert.Equal(source.SourceId, pin.Source.SourceId);
        Assert.Equal(source.SourceVersion, pin.Source.SourceVersion);
        Assert.Equal(source.DefinitionId, pin.Source.DefinitionId);
        Assert.Equal(source.DefinitionVersionId, pin.Source.DefinitionVersionId);
        Assert.Equal(source.ArtifactVersion, pin.Source.ArtifactVersion);
        Assert.Equal(source.PublicationId, pin.Source.PublicationId);
        Assert.Equal(source.SlotId, pin.Source.SlotId);
    }

    [Fact]
    public async Task Registered_source_enriches_dispatch_node_through_named_event_and_real_workflow_executable_compiler()
    {
        const string activityVersionId = "dispatch-activity-version";
        var workflowVersion = new WorkflowDefinitionVersion("parent-definition", "1.0.0")
        {
            Id = "parent-version",
            Definition = new WorkflowDefinition { Id = "parent-definition", Name = "Parent" },
            State = new WorkflowDefinitionState(
                [],
                new ActivityNode(
                    "dispatch-node",
                    activityVersionId,
                    [new ArgumentState(
                        nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                        new ArgumentValue("child-definition", "Literal"),
                        null,
                        null,
                        null,
                        null)],
                    []),
                [],
                [],
                null,
                null)
        };
        var activityVersion = new ActivityDefinitionVersion("1.0.0", "dispatch-activity-definition")
        {
            Id = activityVersionId,
            Definition = new ActivityDefinition
            {
                Id = "dispatch-activity-definition",
                ActivityTypeKey = DispatchWorkflowConstants.ActivityType,
                Category = "Workflows"
            },
            DescriptorType = "test.dispatch-descriptor",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { type = DispatchWorkflowConstants.ActivityType }),
            Inputs =
            [
                new InputDefinition(
                    nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                    nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                    new TypeReference("String"),
                    null,
                    "Workflow definition",
                    null)
            ]
        };
        var childExecutable = Executable("child-artifact", "content-owner", DispatchNode("child-root", "unused"));
        var childSource = Source("child-source", "child-definition", childExecutable.Identity.ArtifactId);
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executableStore.SaveAsync(childExecutable);
        await sourceStore.SaveAsync(childSource);
        var typeRegistry = new WellKnownTypeRegistry();
        typeRegistry.RegisterType(typeof(string), "String");
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new StubWorkflowVersionStore(workflowVersion));
        services.AddSingleton<IActivityDefinitionVersionStore>(new StubActivityVersionStore(activityVersion));
        services.AddSingleton<IWellKnownTypeRegistry>(typeRegistry);
        services.AddSingleton<IWorkflowExecutableStore>(executableStore);
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(sourceStore);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        new DispatchWorkflowDesignFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.Contains(
            scope.ServiceProvider.GetServices<IExecutableNodeMetadataSource>(),
            source => source is DispatchPinSource);
        Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<OnExecutableNodeMetadataCollecting>>());

        var executable = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(
            new WorkflowExecutableCompileRequest(
                "parent-version",
                WorkflowExecutableReferenceScope.Published,
                Now,
                Now,
                null,
                "artifact-parent-"));

        var serializedPin = executable.RootActivity.Metadata[DispatchWorkflowConstants.PinnedTargetMetadataKey];
        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(serializedPin, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(pin);
        Assert.Equal(childExecutable.Identity, pin.Executable);
        Assert.Equal(WorkflowExecutableSourceProvenance.From(childSource), pin.Source);
    }

    [Fact]
    public async Task Pin_contribution_fails_closed_for_ambiguous_live_sources()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Source("source-a", "child-definition", "artifact-a"));
        await references.SaveAsync(Source("source-b", "child-definition", "artifact-b"));
        var sourceProvider = new DispatchPinSource(references, executables, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sourceProvider.GetMetadataAsync(Context(node)));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generic_enricher_rejects_two_owners_for_one_node_metadata_key()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var enricher = Enricher(
            new FirstStubSource(new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "first")),
            new SecondStubSource(new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "second")));
        var context = Context(node);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await enricher.EnrichAsync(context.Request, context.Source, node));

        Assert.Contains("unequal values from owners", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generic_enricher_treats_equal_duplicate_contributions_as_idempotent()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var contribution = new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "same");
        var enricher = Enricher(new FirstStubSource(contribution), new SecondStubSource(contribution));
        var context = Context(node);

        var enriched = await enricher.EnrichAsync(context.Request, context.Source, node);

        Assert.Equal("same", enriched.Metadata["shared-key"]);
    }

    private static ExecutableNodeMetadataContext Context(ExecutableNode node)
    {
        var request = new WorkflowExecutableCompileRequest(
            "parent-version",
            WorkflowExecutableReferenceScope.Published,
            Now,
            Now,
            null,
            "artifact-");
        var source = new WorkflowExecutableCompileSource(
            "parent-definition",
            "parent-version",
            "1.0.0",
            WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion",
            "parent-version",
            "1.0.0");
        return new ExecutableNodeMetadataContext(request, source, node);
    }

    private static ExecutableNodeMetadataEnricher Enricher(params IExecutableNodeMetadataSource[] sources) =>
        new(new CollectingInlineEventPublisher(sources));

    private static ExecutableNode DispatchNode(string nodeId, string definitionId) =>
        new(
            nodeId,
            nodeId,
            DispatchWorkflowConstants.ActivityType,
            "1.0.0",
            "CLR",
            JsonSerializer.SerializeToElement(new { type = DispatchWorkflowConstants.ActivityType }),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["WorkflowDefinitionId"] = new(
                    "WorkflowDefinitionId",
                    RuntimeInputBindingSource.Literal,
                    JsonSerializer.SerializeToElement(definitionId))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["authoredNodeId"] = nodeId });

    private static WorkflowExecutable Executable(string artifactId, string definitionId, ExecutableNode root) =>
        new(
            new WorkflowExecutableIdentity(artifactId, definitionId, "definition-version", "1.0.0", $"sha256:{artifactId}"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>());

    private static WorkflowExecutableSourceReference Source(
        string sourceReferenceId,
        string definitionId,
        string artifactId,
        WorkflowExecutableReferenceScope scope = WorkflowExecutableReferenceScope.Published) =>
        new(
            sourceReferenceId,
            artifactId,
            "WorkflowDefinitionVersion",
            $"{definitionId}-version",
            "1.0.0",
            definitionId,
            $"{definitionId}-version",
            "1.0.0",
            Now,
            scope == WorkflowExecutableReferenceScope.Published ? Now : null,
            scope);

    private static WorkflowDefinition Definition(string id, string name) => new() { Id = id, Name = name };

    private sealed class StubDefinitionStore(params WorkflowDefinition[] definitions) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.Single(definition => definition.Id == id));

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.SingleOrDefault(definition => definition.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(definitions);
    }

    private sealed class StubWorkflowVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(version);

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version.Id == versionId ? version : null);

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Unknown workflow version '{versionId}'.");

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version.DefinitionId == definitionId ? version : null);

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(version.DefinitionId == definitionId ? [version] : []);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey);
    }

    private sealed class StubActivityVersionStore(ActivityDefinitionVersion version) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetWithDefinitionAsync(versionId, cancellationToken);

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Unknown activity version '{versionId}'.");

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersion?>(
                version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey ? version : null);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(version.DefinitionId == definitionId ? [version] : []);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(
                definitionIds.Contains(version.DefinitionId, StringComparer.Ordinal) ? [version] : []);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([version]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private abstract class StubMetadataSource(params ExecutableNodeMetadataContribution[] contributions) : IExecutableNodeMetadataSource
    {
        public ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
            ExecutableNodeMetadataContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ExecutableNodeMetadataContribution>>(contributions);
    }

    private sealed class FirstStubSource(params ExecutableNodeMetadataContribution[] contributions) : StubMetadataSource(contributions)
    {
    }

    private sealed class SecondStubSource(params ExecutableNodeMetadataContribution[] contributions) : StubMetadataSource(contributions)
    {
    }

    private sealed class CollectingInlineEventPublisher(IEnumerable<IExecutableNodeMetadataSource> sources) : IInlineEventPublisher
    {
        private readonly CollectExecutableNodeMetadata _handler = new(sources);

        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            _handler.Handle(Assert.IsType<OnExecutableNodeMetadataCollecting>(@event), cancellationToken);
    }
}
