using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Behavioral tests for <see cref="WorkflowExecutableInspector"/> — the projection behind the executables
/// list/detail bridge endpoints (#598 P1). The inspector is fed the in-memory artifact + reference stores only;
/// it never touches a workflow-definition store, which the self-containment test proves with a throwing fake.
/// </summary>
public sealed class WorkflowExecutableInspectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references = new();
    private readonly TimeProvider _clock = new FixedTimeProvider(Now);

    private WorkflowExecutableInspector Inspector => new(_executables, _references, _clock);

    [Fact]
    public async Task List_GroupsPublishesFromTwoVersionsUnderOneArtifact()
    {
        // Same behavior published from v1 then v2 → one artifact row, two references (newest-first).
        await SaveExecutableAsync("artifact-a");
        await SaveReferenceAsync("ref-1", "artifact-a", "1.0.0", "version-1", publishedAt: Now.AddHours(-2));
        await SaveReferenceAsync("ref-2", "artifact-a", "2.0.0", "version-2", publishedAt: Now.AddHours(-1));

        var view = await Inspector.ListAsync();

        var row = Assert.Single(view.Executables);
        Assert.Equal("artifact-a", row.ArtifactId);
        Assert.Equal(["ref-2", "ref-1"], row.References.Select(r => r.SourceReferenceId));
        // Top-level convenience fields mirror the newest reference (legacy summary compatibility).
        Assert.Equal("2.0.0", row.ArtifactVersion);
        Assert.Equal("version-2", row.DefinitionVersionId);
    }

    [Fact]
    public async Task List_DefaultScope_ExcludesTestRunOnlyArtifacts()
    {
        await SaveExecutableAsync("artifact-published");
        await SaveReferenceAsync("ref-pub", "artifact-published", "1.0.0", "version-1", publishedAt: Now);

        await SaveExecutableAsync("artifact-testrun");
        await SaveReferenceAsync("ref-test", "artifact-testrun", "draft", "draft:x", scope: WorkflowExecutableReferenceScope.TestRun, expiresAt: Now.AddHours(1));

        var published = await Inspector.ListAsync(WorkflowExecutableListScope.Published);
        Assert.Equal(["artifact-published"], published.Executables.Select(r => r.ArtifactId));

        var testRuns = await Inspector.ListAsync(WorkflowExecutableListScope.TestRuns);
        Assert.Equal(["artifact-testrun"], testRuns.Executables.Select(r => r.ArtifactId));

        var all = await Inspector.ListAsync(WorkflowExecutableListScope.All);
        Assert.Equal(2, all.Executables.Count);
    }

    [Fact]
    public async Task List_RetiredReferences_AppearOnlyWhenRequested()
    {
        await SaveExecutableAsync("artifact-a");
        await SaveReferenceAsync("ref-live", "artifact-a", "1.0.0", "version-1", publishedAt: Now.AddHours(-2));
        await SaveReferenceAsync("ref-dead", "artifact-a", "2.0.0", "version-2", publishedAt: Now.AddHours(-1), deletedAt: Now.AddMinutes(-5));

        var live = await Inspector.ListAsync();
        var liveRow = Assert.Single(live.Executables);
        Assert.Equal(["ref-live"], liveRow.References.Select(r => r.SourceReferenceId));

        var withRetired = await Inspector.ListAsync(includeRetired: true);
        var retiredRow = Assert.Single(withRetired.Executables);
        Assert.Equal(["ref-dead", "ref-live"], retiredRow.References.Select(r => r.SourceReferenceId));
    }

    [Fact]
    public async Task List_ArtifactWithOnlyRetiredReferences_HiddenByDefault_VisibleWithRetired()
    {
        await SaveExecutableAsync("artifact-a");
        await SaveReferenceAsync("ref-dead", "artifact-a", "1.0.0", "version-1", publishedAt: Now.AddHours(-1), deletedAt: Now.AddMinutes(-5));

        Assert.Empty((await Inspector.ListAsync()).Executables);
        Assert.Single((await Inspector.ListAsync(includeRetired: true)).Executables);
    }

    [Fact]
    public async Task Detail_ReturnsNodeTreeCountsAndInputBindingSummaries()
    {
        await SaveExecutableAsync("artifact-a", root: RootWithChild());
        await SaveReferenceAsync("ref-1", "artifact-a", "1.0.0", "version-1", publishedAt: Now, layout: [new WorkflowExecutableLayoutRecord("root", 10, 20)]);

        var detail = await Inspector.GetAsync("artifact-a");

        Assert.NotNull(detail);
        Assert.Equal("artifact-a", detail!.ArtifactId);
        Assert.Equal(2, detail.NodeCount);
        Assert.Equal("root", detail.RootActivity.ExecutableNodeId);
        var slot = Assert.Single(detail.RootActivity.ChildSlots);
        var child = Assert.Single(slot.Activities);
        Assert.Equal("child", child.ExecutableNodeId);
        var binding = Assert.Single(child.InputBindings);
        Assert.Equal("Text", binding.InputName);
        Assert.Equal(nameof(RuntimeInputBindingSource.Literal), binding.Source);
        Assert.Equal("hello", binding.Summary);
    }

    [Fact]
    public async Task Detail_ProjectsOnlyFlowchartConnections_WithoutLeakingExecutablePayloads()
    {
        const string descriptorSecret = "descriptor-secret-must-not-leak";
        const string flowchartStructureSecret = "flowchart-structure-secret-must-not-leak";
        const string nonFlowchartStructureSecret = "non-flowchart-structure-secret-must-not-leak";

        var source = Node("source", "WriteLine");
        var target = Node("target", "WriteLine");
        var nonFlowchart = Node(
            "near-match",
            "CustomCanvas",
            structure: new ExecutableActivityStructure(
                "elsa.flowchart.structure.v2",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new[]
                    {
                        new
                        {
                            source = new { nodeId = "must-not-project", port = "Done" },
                            target = new { nodeId = "must-not-project-either", port = "Done" }
                        }
                    },
                    secret = nonFlowchartStructureSecret
                })));
        var root = Node(
            "root",
            "Flowchart",
            [new ExecutableChildSlot("Flowchart.Activities", [source, target, nonFlowchart])],
            new ExecutableActivityStructure(
                "elsa.flowchart.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new[]
                    {
                        new
                        {
                            source = new { nodeId = "source", port = "Approved" },
                            target = new { nodeId = "target", port = "Input" },
                            vertices = new[] { new { x = 120.5, y = 64.25 }, new { x = 240.0, y = 96.0 } }
                        }
                    },
                    nodeMetadata = new { secret = flowchartStructureSecret }
                })),
            JsonSerializer.SerializeToElement(new { secret = descriptorSecret }));
        await SaveExecutableAsync("artifact-a", root);

        var detail = await Inspector.GetAsync("artifact-a");

        var connection = Assert.Single(detail!.RootActivity.Connections!);
        Assert.Equal("source", connection.Source.NodeId);
        Assert.Equal("Approved", connection.Source.Port);
        Assert.Equal("target", connection.Target.NodeId);
        Assert.Equal("Input", connection.Target.Port);
        Assert.Collection(
            connection.Vertices!,
            vertex =>
            {
                Assert.Equal(120.5, vertex.X);
                Assert.Equal(64.25, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal(240.0, vertex.X);
                Assert.Equal(96.0, vertex.Y);
            });

        var projectedNearMatch = Assert.Single(detail.RootActivity.ChildSlots).Activities[2];
        Assert.Null(projectedNearMatch.Connections);

        var wireJson = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var wireRoot = wireJson.GetProperty("rootActivity");
        Assert.True(wireRoot.TryGetProperty("connections", out _));
        var wireNearMatch = wireRoot.GetProperty("childSlots")[0].GetProperty("activities")[2];
        Assert.False(wireNearMatch.TryGetProperty("connections", out _));
        Assert.False(wireRoot.TryGetProperty("descriptorPayload", out _));
        Assert.False(wireRoot.TryGetProperty("structure", out _));
        Assert.DoesNotContain(descriptorSecret, wireJson.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(flowchartStructureSecret, wireJson.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(nonFlowchartStructureSecret, wireJson.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_ChoosesNewestLiveReference_ForLayout_ByDefault()
    {
        await SaveExecutableAsync("artifact-a");
        await SaveReferenceAsync("ref-old", "artifact-a", "1.0.0", "version-1", publishedAt: Now.AddHours(-2), layout: [new WorkflowExecutableLayoutRecord("root", 1, 1)]);
        await SaveReferenceAsync("ref-new", "artifact-a", "2.0.0", "version-2", publishedAt: Now.AddHours(-1), layout: [new WorkflowExecutableLayoutRecord("root", 2, 2)]);

        var detail = await Inspector.GetAsync("artifact-a");

        Assert.NotNull(detail!.ChosenReference);
        Assert.Equal("ref-new", detail.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest-live", detail.ChosenReference.Selection);
        Assert.Equal(2, Assert.Single(detail.ChosenReference.Layout).X);
    }

    [Fact]
    public async Task Detail_HonorsExplicitRefHint_AndFallsBackWhenUnknown()
    {
        await SaveExecutableAsync("artifact-a");
        await SaveReferenceAsync("ref-old", "artifact-a", "1.0.0", "version-1", publishedAt: Now.AddHours(-2), layout: [new WorkflowExecutableLayoutRecord("root", 1, 1)]);
        await SaveReferenceAsync("ref-new", "artifact-a", "2.0.0", "version-2", publishedAt: Now.AddHours(-1), layout: [new WorkflowExecutableLayoutRecord("root", 2, 2)]);

        var explicitPick = await Inspector.GetAsync("artifact-a", "ref-old");
        Assert.Equal("ref-old", explicitPick!.ChosenReference!.SourceReferenceId);
        Assert.Equal("requested", explicitPick.ChosenReference.Selection);

        // An unresolvable hint falls back to the default newest-live selection rather than 404ing.
        var fallback = await Inspector.GetAsync("artifact-a", "does-not-exist");
        Assert.Equal("ref-new", fallback!.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest-live", fallback.ChosenReference.Selection);
    }

    [Fact]
    public async Task Detail_UnknownArtifact_ReturnsNull()
    {
        Assert.Null(await Inspector.GetAsync("missing"));
    }

    [Fact]
    public async Task Detail_IsSelfContained_NeverConsultsWorkflowDefinitionStore()
    {
        // The inspector's constructor takes no definition store — self-containment is structural. This test proves
        // it end-to-end: even wired alongside a throwing definition store, a full detail read succeeds because that
        // store is never touched (an artifact must be inspectable wherever it was promoted, plan §3/§4).
        var throwingDefinitionStore = new ThrowingWorkflowDefinitionStore();
        await SaveExecutableAsync("artifact-a", root: RootWithChild());
        await SaveReferenceAsync("ref-1", "artifact-a", "1.0.0", "version-1", publishedAt: Now);

        var detail = await Inspector.GetAsync("artifact-a");

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.NodeCount);
        // Sanity: the fake really does throw if consulted, so the passing read above is meaningful.
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwingDefinitionStore.FindByIdAsync("artifact-a", CancellationToken.None));
    }

    private async Task SaveExecutableAsync(string artifactId, ExecutableNode? root = null) =>
        await _executables.SaveAsync(new WorkflowExecutable(
            new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", $"sha256:{artifactId}"),
            root ?? Node("root", "Flowchart"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now.AddHours(-3),
            new Dictionary<string, string>()));

    private async Task SaveReferenceAsync(
        string sourceReferenceId,
        string artifactId,
        string artifactVersion,
        string definitionVersionId,
        WorkflowExecutableReferenceScope scope = WorkflowExecutableReferenceScope.Published,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? deletedAt = null,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout = null) =>
        await _references.SaveAsync(new WorkflowExecutableSourceReference(
            sourceReferenceId,
            artifactId,
            SourceKind: "WorkflowDefinition",
            SourceId: "definition-1",
            SourceVersion: definitionVersionId,
            DefinitionId: "definition-1",
            DefinitionVersionId: definitionVersionId,
            ArtifactVersion: artifactVersion,
            CreatedAt: publishedAt ?? Now,
            PublishedAt: publishedAt,
            Scope: scope,
            ExpiresAt: expiresAt,
            DeletedAt: deletedAt,
            DeletedReason: deletedAt is null ? null : "test",
            Layout: layout));

    private static ExecutableNode Node(
        string id,
        string activityType,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        JsonElement? descriptorPayload = null) =>
        new(
            executableNodeId: id,
            authoredActivityId: $"authored-{id}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: $"{activityType}Descriptor",
            descriptorPayload: descriptorPayload ?? JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableNode RootWithChild()
    {
        var child = new ExecutableNode(
            executableNodeId: "child",
            authoredActivityId: "authored-child",
            activityType: "WriteLine",
            activityTypeVersion: "1.0.0",
            descriptorType: "WriteLineDescriptor",
            descriptorPayload: JsonDocument.Parse("{}").RootElement,
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Text"] = new(
                    inputName: "Text",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonDocument.Parse("\"hello\"").RootElement)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return Node("root", "Flowchart", [new ExecutableChildSlot("Activities", [child])]);
    }

    /// <summary>A workflow-definition store that throws on any read — proves the inspector never consults it.</summary>
    private sealed class ThrowingWorkflowDefinitionStore : IWorkflowDefinitionStore
    {
        private static InvalidOperationException Boom() => new("The inspector must not consult the workflow-definition store.");

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw Boom();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw Boom();
    }

    /// <summary>Minimal fixed-instant <see cref="TimeProvider"/>: enough for the inspector's liveness/ordering.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
