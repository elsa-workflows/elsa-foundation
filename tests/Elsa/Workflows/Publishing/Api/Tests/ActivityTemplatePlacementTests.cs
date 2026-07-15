using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityTemplatePlacementTests
{
    [Fact]
    public async Task Placement_preserves_nested_multi_slot_structure_and_exact_catalog_identity()
    {
        var fixture = Fixture.Create();
        var placement = await fixture.Placer.PlaceAsync(fixture.Request);

        var graphRoot = Assert.Single(Assert.Single(placement.Root.ChildSlots, x => x.Name == "activity-graph").Activities);
        Assert.Equal("type.if", graphRoot.ActivityType);
        Assert.Equal("version-if", graphRoot.Descriptor.Payload.GetProperty("definitionVersionId").GetString());
        var thenNode = Assert.Single(Assert.Single(graphRoot.ChildSlots, x => x.Name == "If.Then").Activities);
        var elseNode = Assert.Single(Assert.Single(graphRoot.ChildSlots, x => x.Name == "If.Else").Activities);
        Assert.Equal("type.then", thenNode.ActivityType);
        Assert.Equal("type.else", elseNode.ActivityType);
        Assert.All(new[] { placement.Root, graphRoot, thenNode, elseNode }, x => Assert.Equal(69, x.ExecutableNodeId.Length));
        Assert.Equal(graphRoot.ExecutableNodeId, placement.Root.Descriptor.Payload.GetProperty("entryNodeId").GetString());
        Assert.Equal(4, placement.LayoutSidecar.BoundarySegments.Count);
    }

    [Fact]
    public async Task Placement_rejects_a_generated_identity_collision()
    {
        var fixture = Fixture.Create(new ConstantHasher());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Placer.PlaceAsync(fixture.Request).AsTask());
    }

    [Fact]
    public async Task Repeated_placements_are_deterministic_and_namespaced_by_invocation_origin()
    {
        var fixture = Fixture.Create();
        var first = await fixture.Placer.PlaceAsync(fixture.Request);
        var repeated = await fixture.Placer.PlaceAsync(fixture.Request);
        var moved = await fixture.Placer.PlaceAsync(fixture.Request with
        {
            InvocationOrigin = new ActivityInvocationOrigin([
                new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-version"),
                new(ActivityInvocationOriginSegmentKind.AuthoredNode, "another-composite-node"),
                new(ActivityInvocationOriginSegmentKind.TemplateBoundary, "version-top")
            ])
        });

        Assert.Equal(first.Root.ExecutableNodeId, repeated.Root.ExecutableNodeId);
        Assert.NotEqual(first.Root.ExecutableNodeId, moved.Root.ExecutableNodeId);
        Assert.All(Flatten(first.Root), x => Assert.Matches("^node-[0-9a-f]{64}$", x.ExecutableNodeId));
    }

    [Fact]
    public async Task Repeated_resumable_placements_keep_local_target_identity_but_namespace_canonical_ids()
    {
        var root = Node("wait");
        var template = new ExecutableActivityTemplate(
            "template-wait", "hash-wait", root,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new("resume-target:delivery", root.ExecutableNodeId, "OnDelivery", new Dictionary<string, string>())
            },
            [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var publication = Publication("definition-wait", "version-wait", "type.wait", template);
        var reference = Reference(publication, ExecutableLayoutSidecar.Empty);
        var placer = new ActivityTemplatePlacer(
            new PublicationStore([publication]), new TemplateReader([template]), new SourceReader([reference]), new Sha256ActivityPlacementHasher());
        ActivityTemplatePlacementRequest Request(string authoredNode) => new(
            publication, template, reference,
            new([
                new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow"),
                new(ActivityInvocationOriginSegmentKind.AuthoredNode, authoredNode),
                new(ActivityInvocationOriginSegmentKind.TemplateBoundary, publication.DefinitionVersionId)
            ]),
            publication.ActivityTypeKey, new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>());

        var first = Assert.Single((await placer.PlaceAsync(Request("first"))).ResumeTargets.Values);
        var second = Assert.Single((await placer.PlaceAsync(Request("second"))).ResumeTargets.Values);

        Assert.NotEqual(first.ResumeTargetId, second.ResumeTargetId);
        Assert.Equal("resume-target:delivery", first.LocalResumeTargetId);
        Assert.Equal(first.LocalResumeTargetId, second.LocalResumeTargetId);
    }

    [Fact]
    public async Task Unrelated_sibling_changes_do_not_renumber_an_existing_subtree()
    {
        var beforeFixture = Fixture.Create();
        var afterFixture = Fixture.Create(includeUnrelatedRoot: true);
        var before = await beforeFixture.Placer.PlaceAsync(beforeFixture.Request);
        var after = await afterFixture.Placer.PlaceAsync(afterFixture.Request);

        static ExecutableNode Then(ExecutableNode root)
        {
            var graph = Assert.Single(root.ChildSlots, x => x.Name == "activity-graph");
            var ifNode = Assert.Single(graph.Activities, x => x.ActivityType == "type.if");
            return Assert.Single(ifNode.ChildSlots, x => x.Name == "If.Then").Activities.Single();
        }
        Assert.Equal(Then(before.Root).ExecutableNodeId, Then(after.Root).ExecutableNodeId);
    }

    [Fact]
    public async Task Retired_exact_child_closed_into_selected_parent_remains_placeable()
    {
        var fixture = Fixture.Create(nestedLifecycle: ActivityDefinitionVersionLifecycle.Retired);

        var placement = await fixture.Placer.PlaceAsync(fixture.Request);

        Assert.NotNull(placement.Root);
    }

    [Fact]
    public async Task Revoked_closed_child_and_retired_direct_selection_are_blocked()
    {
        var revoked = Fixture.Create(nestedLifecycle: ActivityDefinitionVersionLifecycle.Revoked);
        var retiredRoot = Fixture.Create();
        retiredRoot.Request.Publication.Lifecycle = ActivityDefinitionVersionLifecycle.Retired;

        await Assert.ThrowsAsync<InvalidOperationException>(() => revoked.Placer.PlaceAsync(revoked.Request).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => retiredRoot.Placer.PlaceAsync(retiredRoot.Request).AsTask());
    }

    [Fact]
    public async Task Deep_dependency_traversal_is_iterative_and_observes_cancellation()
    {
        const int depth = 1500;
        var templates = new List<ExecutableActivityTemplate>();
        var publications = new List<ActivityDefinitionVersionPublication>();
        var references = new List<WorkflowExecutableSourceReference>();
        for (var index = depth - 1; index >= 0; index--)
        {
            var child = index == depth - 1
                ? Array.Empty<ExecutableActivityTemplateDependency>()
                : [new ExecutableActivityTemplateDependency($"definition-{index + 1}", $"version-{index + 1}", "1.0.0", $"template-{index + 1}", $"hash-{index + 1}", $"occurrence-{index + 1}", ActivityInvocationOrigin.Empty)];
            var template = new ExecutableActivityTemplate(
                $"template-{index}", $"hash-{index}", Node($"local-{index}"),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), child, [], [], "fingerprint",
                new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            var publication = Publication($"definition-{index}", $"version-{index}", $"type-{index}", template);
            templates.Add(template);
            publications.Add(publication);
            references.Add(Reference(publication, ExecutableLayoutSidecar.Empty));
        }
        var placer = new ActivityTemplatePlacer(
            new PublicationStore(publications), new TemplateReader(templates), new SourceReader(references), new Sha256ActivityPlacementHasher());
        var rootPublication = publications.Single(x => x.DefinitionVersionId == "version-0");
        var rootTemplate = templates.Single(x => x.TemplateId == "template-0");
        var request = new ActivityTemplatePlacementRequest(
            rootPublication, rootTemplate, references.Single(x => x.SourceReferenceId == rootPublication.SourceReferenceId),
            new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow")]), "type-0",
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>());

        var result = await placer.PlaceAsync(request);
        Assert.Equal(depth, Flatten(result.Root).Count());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => placer.PlaceAsync(request, cancellation.Token).AsTask());
    }

    private sealed record Fixture(ActivityTemplatePlacer Placer, ActivityTemplatePlacementRequest Request)
    {
        public static Fixture Create(
            IActivityPlacementHasher? hasher = null,
            bool includeUnrelatedRoot = false,
            ActivityDefinitionVersionLifecycle nestedLifecycle = ActivityDefinitionVersionLifecycle.Active)
        {
            var thenTemplate = Template("template-then", "hash-then", Node("then"));
            var elseTemplate = Template("template-else", "hash-else", Node("else"));
            var ifTemplate = Template("template-if", "hash-if", Node("if"));
            var unrelatedTemplate = Template("template-unrelated", "hash-unrelated", Node("unrelated"));
            var graphDescriptor = new RuntimeActivityDescriptor("elsa.graph-activity", "1", Json("""{"entryOccurrenceId":"if"}"""));
            var graphRoot = new ExecutableNode("graph", "graph", "elsa.graph-activity", "1", graphDescriptor,
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
            var dependencies = new List<ExecutableActivityTemplateDependency>
            {
                new("definition-if", "version-if", "1.0.0", "template-if", "hash-if", "if", ActivityInvocationOrigin.Empty, null, "activity-graph", 0),
                new("definition-then", "version-then", "1.0.0", "template-then", "hash-then", "then", ActivityInvocationOrigin.Empty, "if", "If.Then", 0),
                new("definition-else", "version-else", "1.0.0", "template-else", "hash-else", "else", ActivityInvocationOrigin.Empty, "if", "If.Else", 0)
            };
            if (includeUnrelatedRoot)
                dependencies.Add(new("definition-unrelated", "version-unrelated", "1.0.0", "template-unrelated", "hash-unrelated", "unrelated", ActivityInvocationOrigin.Empty, null, "activity-graph", 1));
            var topTemplate = new ExecutableActivityTemplate(
                "template-top", "hash-top", graphRoot, new Dictionary<string, WorkflowExecutableResumeTarget>(),
                dependencies, [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);

            var publications = new[]
            {
                Publication("definition-top", "version-top", "type.graph", topTemplate),
                Publication("definition-if", "version-if", "type.if", ifTemplate),
                Publication("definition-then", "version-then", "type.then", thenTemplate),
                Publication("definition-else", "version-else", "type.else", elseTemplate),
                Publication("definition-unrelated", "version-unrelated", "type.unrelated", unrelatedTemplate)
            };
            publications.Single(x => x.DefinitionVersionId == "version-then").Lifecycle = nestedLifecycle;
            var references = publications.Select(x => Reference(x, new ExecutableLayoutSidecar([
                new(x.DefinitionVersionId, ActivityInvocationOrigin.Empty, x.TemplateHash,
                    [new("local", "authored", "local", 10, 20)], [])
            ]))).ToArray();
            var publicationStore = new PublicationStore(publications);
            var templateStore = new TemplateReader([topTemplate, ifTemplate, thenTemplate, elseTemplate, unrelatedTemplate]);
            var sourceStore = new SourceReader(references);
            var placer = new ActivityTemplatePlacer(publicationStore, templateStore, sourceStore, hasher ?? new Sha256ActivityPlacementHasher());
            var origin = new ActivityInvocationOrigin([
                new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-version"),
                new(ActivityInvocationOriginSegmentKind.AuthoredNode, "composite-node"),
                new(ActivityInvocationOriginSegmentKind.TemplateBoundary, "version-top")
            ]);
            return new(placer, new(publications[0], topTemplate, references[0], origin, "type.graph",
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>()));
        }
    }

    private static ExecutableActivityTemplate Template(string id, string hash, ExecutableNode root) => new(
        id, hash, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint",
        new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);

    private static ExecutableNode Node(string id) => new(
        id, id, "local", "1", new("local", "1", Json("{}")),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());

    private static ActivityDefinitionVersionPublication Publication(
        string definitionId,
        string versionId,
        string activityTypeKey,
        ExecutableActivityTemplate template) => new()
    {
        Id = versionId,
        DefinitionId = definitionId,
        DefinitionVersionId = versionId,
        Version = "1.0.0",
        ActivityTypeKey = activityTypeKey,
        Contract = new("1", [], [], []),
        Provider = new("test", "1", Json("{}")),
        TemplateId = template.TemplateId,
        TemplateHash = template.TemplateHash,
        SourceReferenceId = $"source-{versionId}",
        ProviderFingerprint = "fingerprint",
        DirectDependencyCount = template.DirectDependencies.Count,
        ClosedTemplateCount = template.ClosedTemplates.Count,
        RuntimeRequirements = [],
        Lifecycle = ActivityDefinitionVersionLifecycle.Active
    };

    private static WorkflowExecutableSourceReference Reference(
        ActivityDefinitionVersionPublication publication,
        ExecutableLayoutSidecar sidecar) => new(
        publication.SourceReferenceId, publication.TemplateId, "ActivityDefinitionVersion", publication.DefinitionVersionId,
        publication.Version, publication.DefinitionId, publication.DefinitionVersionId, publication.Version,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WorkflowExecutableReferenceScope.Published, LayoutSidecar: sidecar);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static IReadOnlyList<ExecutableNode> Flatten(ExecutableNode root)
    {
        var result = new List<ExecutableNode>();
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            result.Add(node);
            foreach (var child in node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                stack.Push(child);
        }
        return result;
    }

    private sealed class ConstantHasher : IActivityPlacementHasher
    {
        public string ComputeHash(byte[] canonicalOrigin) => new('a', 64);
    }

    private sealed class PublicationStore(IEnumerable<ActivityDefinitionVersionPublication> values) : IActivityDefinitionVersionPublicationStore
    {
        private readonly IReadOnlyDictionary<string, ActivityDefinitionVersionPublication> _values = values.ToDictionary(x => x.DefinitionVersionId);
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(definitionVersionId));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(_values.Values.Where(x => x.DefinitionId == definitionId).ToArray());
    }

    private sealed class TemplateReader(IEnumerable<ExecutableActivityTemplate> values) : IExecutableActivityTemplateReader
    {
        private readonly IReadOnlyDictionary<string, ExecutableActivityTemplate> _values = values.ToDictionary(x => x.TemplateId);
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_values.GetValueOrDefault(templateId));
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult(_values.Values.SingleOrDefault(x => x.TemplateHash == templateHash));
    }

    private sealed class SourceReader(IEnumerable<WorkflowExecutableSourceReference> values) : IWorkflowExecutableSourceReferenceReader
    {
        private readonly IReadOnlyDictionary<string, WorkflowExecutableSourceReference> _values = values.ToDictionary(x => x.SourceReferenceId);
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_values.GetValueOrDefault(sourceReferenceId));
        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>(_values.Values.Where(x => x.ArtifactId == artifactId).ToArray());
        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(WorkflowExecutableReferenceScope? scope = null, bool liveOnly = false, DateTimeOffset? now = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>(_values.Values.ToArray());
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(IEnumerable<string> artifactIds, DateTimeOffset now, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<string>>([]);
    }
}
