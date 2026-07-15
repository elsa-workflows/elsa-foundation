using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
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
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishWorkflowRequestHandlerTests
{
    private const string UnknownStructureKind = "test.opaque.structure";
    private const string UnknownStructureSchemaVersion = "1.0.0";

    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
    private readonly ActivityDefinitionVersion _flowchartActivity = ActivityVersion("activity-flowchart", typeof(FlowchartActivity).FullName!);
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();

    [Fact]
    public async Task PublishesRootActivityIntoExecutableArtifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Equal("write-one", view.RootActivityId);
        Assert.Equal(1, view.NodeCount);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
        Assert.Equal("one", executable.NodesById["write-one"].InputBindings["Text"].LiteralValue!.Value.GetString());
        Assert.Equal($"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}", executable.NodesById["write-one"].InputBindings["Text"].Metadata["typeName"]);
    }

    [Fact]
    public async Task SameBehaviorFromTwoDefinitionVersionsYieldsOneArtifactAndTwoReferences()
    {
        // Acceptance 1 (ADR 0038): publishing the same behavior from two different definition versions resolves to
        // ONE content-addressed artifact and appends TWO source references pointing at it.
        var firstVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-A", versionId: "version-A", version: "1.0.0");
        var secondVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-B", versionId: "version-B", version: "3.7.0");

        var first = await Handler(firstVersion).Handle(new PublishWorkflow("version-A"), CancellationToken.None);
        var second = await Handler(secondVersion).Handle(new PublishWorkflow("version-B"), CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        Assert.Single(await _store.ListAsync());

        var references = await _referenceStore.ListByArtifactAsync(first.ArtifactId);
        Assert.Equal(2, references.Count);
        Assert.Equal(["definition-A", "definition-B"], references.Select(r => r.DefinitionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["version-A", "version-B"], references.Select(r => r.DefinitionVersionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(references, reference =>
        {
            Assert.Equal(first.ArtifactId, reference.ArtifactId);
            Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope);
            Assert.NotNull(reference.PublishedAt);
            Assert.Null(reference.ExpiresAt);
        });
    }

    [Fact]
    public async Task RepublishingIdenticalVersionIsIdempotentOnArtifactAndAppendsReference()
    {
        // Acceptance 2 (ADR 0038): republishing an identical version resolves to the same artifact (idempotent, not
        // overwritten) and appends another reference.
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(version);

        var first = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var storedFirst = await _store.FindAsync(first.ArtifactId);
        var second = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var storedSecond = await _store.FindAsync(second.ArtifactId);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Single(await _store.ListAsync());
        // Idempotent: the stored artifact instance is not replaced on republish.
        Assert.Same(storedFirst, storedSecond);
        Assert.Equal(2, (await _referenceStore.ListByArtifactAsync(first.ArtifactId)).Count);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
    }

    [Fact]
    public async Task AppendedReferenceCarriesLayoutRecordsCopiedFromDefinitionVersion()
    {
        // Acceptance 3 (ADR 0039): the appended reference embeds the layout sidecar copied verbatim from the
        // definition version's layout store.
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var additional = JsonSerializer.SerializeToElement(new { color = "blue" });
        var layout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = "version-1",
            Records = [new DesignMetadataRecord("write-one", 12.5, 34.0, 200, 80, additional)]
        };

        var view = await Handler(version, layout, _writeLineActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var reference = Assert.Single(await _referenceStore.ListByArtifactAsync(view.ArtifactId));
        var record = Assert.Single(reference.Layout);
        Assert.Equal("write-one", record.NodeId);
        Assert.Equal(12.5, record.X);
        Assert.Equal(34.0, record.Y);
        Assert.Equal(200, record.Width);
        Assert.Equal(80, record.Height);
        Assert.Equal("blue", record.AdditionalProperties!.Value.GetProperty("color").GetString());
    }

    [Fact]
    public async Task PublishedExecutableArtifactCanBeDispatchedForExecution()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(workflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var dispatcher = new WorkflowStartDispatcher(
            _store,
            _referenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator());

        // The publish above appended a live Published source reference the dispatch gates on (ADR 0040).
        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(published.ArtifactId, "test"));

        Assert.Equal(published.ArtifactId, result.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.NotEmpty(result.WorkflowExecutionId);
    }

    [Fact]
    public async Task PublishesSequenceAuthoredStructureIntoExecutableChildSlotAndStructure()
    {
        var root = SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]);
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _sequenceActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("sequence", view.RootActivityId);
        Assert.Equal(3, view.NodeCount);
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(SequenceActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));

        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(SequenceActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(["write-one", "write-two"], executable.RootActivity.Structure.Payload.GetProperty("activities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task PublishesFlowchartAuthoredStructureIntoExecutableChildSlotAndRuntimeStructure()
    {
        var root = FlowchartNode(
            "flowchart",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ],
            [new FlowchartConnection(new FlowchartEndpoint("write-one", "Done"), new FlowchartEndpoint("write-two", null))],
            "write-one");
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _flowchartActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(FlowchartActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(FlowchartActivity.StructureSchemaVersion, executable.RootActivity.Structure.SchemaVersion);
        Assert.Equal("write-one", executable.RootActivity.Structure.Payload.GetProperty("startNodeId").GetString());
        Assert.False(executable.RootActivity.Structure.Payload.TryGetProperty("activities", out _));
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(FlowchartActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));
    }

    [Fact]
    public async Task PublishesUnknownOpaqueStructureWithoutProjectingChildren()
    {
        var root = Node(
            "opaque",
            structure: new ActivityNodeStructure(
                UnknownStructureKind,
                UnknownStructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { marker = "kept" })));
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Empty(executable.RootActivity.ChildSlots);
        Assert.Equal(UnknownStructureKind, executable.RootActivity.Structure?.Kind);
        Assert.Equal("kept", executable.RootActivity.Structure?.Payload.GetProperty("marker").GetString());
    }

    [Fact]
    public async Task RejectsWorkflowWithoutRootActivity()
    {
        var workflowVersion = WorkflowVersion(rootActivity: null);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("root activity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishesNonLiteralInputAsExpressionBinding()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("\"Hello \" + \"World\"", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        var binding = executable.NodesById["write-one"].InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("JavaScript", binding.Expression!.Language);
        Assert.Equal("\"Hello \" + \"World\"", binding.Expression.Expression);
    }

    [Fact]
    public async Task RejectsExpressionInputWithoutExpressionText()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("no expression text", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownActivityVersionId()
    {
        var workflowVersion = WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("one")], []));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("missing-activity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workflow definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PropagatesTypedCompilationExceptionForUnknownVersionId()
    {
        // #397: publishing a VersionId the store cannot resolve used to bubble a raw ArgumentException from
        // version-source resolution (which ran before the compiler's guarded region) and the handler then
        // rewrapped every compilation failure into a bare ArgumentException, erasing the type. The handler now
        // lets the typed WorkflowExecutableCompilationException propagate untouched.
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("missing-version"), CancellationToken.None));

        Assert.Contains("missing-version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenSequenceOrderChanges()
    {
        var firstWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two")),
                Node("write-three", Text("three"))
            ]));
        var secondWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-three", Text("three")),
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]));
        var firstView = await Handler(firstWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var secondView = await Handler(secondWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(firstView.ArtifactId, secondView.ArtifactId);
        Assert.NotEqual(firstView.ArtifactHash, secondView.ArtifactHash);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenLiteralInputTypeMetadataChanges()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("1")));
        var stringView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("String")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var integerView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("Int32")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(stringView.ArtifactId, integerView.ArtifactId);
        Assert.NotEqual(stringView.ArtifactHash, integerView.ArtifactHash);
    }

    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion) =>
        Handler(workflowVersion, _writeLineActivity);

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activityVersions) =>
        Handler(workflowVersion, layout: null, activityVersions);

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        WorkflowDefinitionVersionLayout? layout,
        params ActivityDefinitionVersion[] activityVersions) =>
        new(
            TestCompiler.Create(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore(activityVersions.ToList()),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            _store,
            _referenceStore,
            new WorkflowTriggerIndexer(
                new WorkflowTriggerBindingExtractor([]),
                new InMemoryWorkflowTriggerBindingStore()),
            new FakeLayoutStore(layout));

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        WorkflowVersion(rootActivity, "definition-1", "version-1", "1.0.0");

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity, string definitionId, string versionId, string version) =>
        new(definitionId, version)
        {
            Id = versionId,
            Definition = new WorkflowDefinition { Id = definitionId, Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        Node(nodeId, structure: null, inputs);

    private static ActivityNode Node(
        string nodeId,
        ActivityNodeStructure? structure,
        params WorkflowArgumentState[] inputs) =>
        new(
            nodeId,
            "activity-write-line",
            inputs,
            Outputs: [],
            Structure: structure);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities) =>
        new(
            nodeId,
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities))));

    private static ActivityNode FlowchartNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId) =>
        new(
            nodeId,
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(activities, connections, startNodeId))));

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(id, "Test.WriteLine", [new InputDefinition(inputName, inputName, inputType, null, inputName, null)]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey) =>
        ActivityVersion(id, activityTypeKey, []);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey, IReadOnlyCollection<InputDefinition> inputs) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = inputs
        };

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeLayoutStore(WorkflowDefinitionVersionLayout? layout) : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(layout);
    }
}
