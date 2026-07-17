using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class GraphActivityProviderTests
{
    private readonly GraphActivityProvider _provider = new(
        new TestActivityStructureService(),
        new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()));

    [Fact]
    public void Schema_one_round_trip_is_canonical_and_preserves_array_order()
    {
        var first = ParseJson("""
            {
              "variables": [{
                "type": { "collectionKind": "Single", "alias": "Decimal" },
                "storageDriverKey": "elsa.json",
                "name": "RunningTotal",
                "referenceKey": "running-total",
                "initialValue": { "value": { "b": 2, "a": 1.0 }, "syntax": "Literal" }
              }],
              "rootActivity": {
                "structure": null,
                "outputs": [],
                "nodeId": "graph-root",
                "inputs": [],
                "activityVersionId": "activity-ver-sequence-1"
              },
              "outputMappings": [{
                "source": { "value": "getVariable('RunningTotal')", "syntax": "JavaScript" },
                "outputReferenceKey": "total"
              }]
            }
            """);
        var second = ParseJson("""
            {
              "outputMappings": [{
                "outputReferenceKey": "total",
                "source": { "syntax": "JavaScript", "value": "getVariable('RunningTotal')" }
              }],
              "rootActivity": {
                "activityVersionId": "activity-ver-sequence-1",
                "inputs": [],
                "nodeId": "graph-root",
                "outputs": [],
                "structure": null
              },
              "variables": [{
                "initialValue": { "syntax": "Literal", "value": { "a": 1, "b": 2 } },
                "referenceKey": "running-total",
                "name": "RunningTotal",
                "storageDriverKey": "elsa.json",
                "type": { "alias": "Decimal", "collectionKind": "Single" }
              }]
            }
            """);

        Assert.True(ActivityGraphManifest.TryParse(first, out var firstManifest, out var firstErrors));
        Assert.True(ActivityGraphManifest.TryParse(second, out var secondManifest, out var secondErrors));
        Assert.Empty(firstErrors);
        Assert.Empty(secondErrors);
        Assert.Equal(CollectionKind.Single, firstManifest!.Variables[0].Type.CollectionKind);
        Assert.Equal(firstManifest.ToCanonicalUtf8Bytes(), secondManifest!.ToCanonicalUtf8Bytes());
    }

    [Fact]
    public async Task Contract_proposal_does_not_duplicate_public_contract_and_seeds_done()
    {
        var proposal = await _provider.ProposeContractAsync(new(CreateManifest(), new ActivityContract("1", [], [], [])));

        Assert.Empty(proposal.Diagnostics);
        var change = Assert.Single(proposal.Changes);
        Assert.Equal(ActivityContractProposalOperation.Add, change.Operation);
        var outcome = Assert.IsType<ActivityOutcomeContract>(change.Outcome);
        Assert.Equal("done", outcome.ReferenceKey);
        Assert.True(outcome.IsEmitted);
        var required = Assert.Single(_provider.AuthoringCapabilities.ContractConstraints.RequiredOutcomes);
        Assert.Equal(outcome, required);
    }

    [Fact]
    public async Task Validation_rejects_forbidden_capabilities_and_missing_required_output_deterministically()
    {
        var manifest = CreateManifest("""
            {
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "isTrigger": true,
                "mutatesWorkflowRoot": true,
                "inputs": [],
                "outputs": [],
                "structure": null
              },
              "variables": [],
              "outputMappings": []
            }
            """);

        var first = await _provider.ValidateAsync(manifest, CreateContract());
        var second = await _provider.ValidateAsync(manifest, CreateContract());

        Assert.Equal(
            first.Select(x => (x.Code, x.Location?.JsonPointer, x.Location?.ReferenceKey)),
            second.Select(x => (x.Code, x.Location?.JsonPointer, x.Location?.ReferenceKey)));
        Assert.Equal(
            [
                "activity.graph.output-mapping-required",
                "activity.graph.trigger-entry-forbidden",
                "activity.graph.workflow-root-mutation-forbidden"
            ],
            first.Select(x => x.Code));
    }

    [Fact]
    public async Task Validation_rejects_malformed_nested_authored_nodes()
    {
        var manifest = CreateManifest("""
            {
              "rootActivity": {
                "nodeId": "root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "Sequence",
                  "schemaVersion": "1",
                  "payload": {
                    "activities": [
                      { "nodeId": "missing-version", "inputs": [], "outputs": [], "structure": null },
                      { "activityVersionId": "activity-ver-missing-node", "inputs": [], "outputs": [], "structure": null }
                    ]
                  }
                }
              },
              "variables": [],
              "outputMappings": [{
                "outputReferenceKey": "total",
                "source": { "syntax": "Literal", "value": 42 }
              }]
            }
            """);

        var diagnostics = await _provider.ValidateAsync(manifest, CreateContract());

        Assert.Equal(
            ["activity.graph.activity-version-required", "activity.graph.node-id-required"],
            diagnostics.Select(x => x.Code));
        Assert.Contains(diagnostics, x => x.Location!.JsonPointer == "/rootActivity/structure/payload/activities/0/activityVersionId");
        Assert.Contains(diagnostics, x => x.Location!.JsonPointer == "/rootActivity/structure/payload/activities/1/nodeId");
    }

    [Fact]
    public async Task Compilation_is_deterministic_and_preserves_exact_dependency_origins()
    {
        var contract = CreateContract();
        var manifest = CreateManifest("""
            {
              "variables": [{
                "referenceKey": "counter",
                "name": "Counter",
                "type": { "alias": "Int64", "collectionKind": "Single" },
                "storageDriverKey": "elsa.json",
                "initialValue": { "syntax": "Literal", "value": 0 }
              }],
              "outputMappings": [{
                "outputReferenceKey": "total",
                "source": { "syntax": "JavaScript", "value": "getVariable('Counter')" }
              }],
              "rootActivity": {
                "structure": {
                  "payload": {
                    "activities": [{
                      "structure": null,
                      "nodeId": "child",
                      "outputs": [],
                      "activityVersionId": "activity-ver-child",
                      "inputs": []
                    }]
                  },
                  "schemaVersion": "1",
                  "kind": "Sequence"
                },
                "outputs": [],
                "inputs": [],
                "activityVersionId": "activity-ver-root",
                "nodeId": "root"
              }
            }
            """);
        var rootDependency = CreateDependency("root", "activity-ver-root", "root-origin");
        var childDependency = CreateDependency("child", "activity-ver-child", "child-origin");
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "2.0.0",
            contract,
            manifest,
            [rootDependency, childDependency],
            "provider-fingerprint");

        var first = await _provider.CompileAsync(request);
        var second = await _provider.CompileAsync(request);

        Assert.Empty(first.Diagnostics);
        Assert.Equal(JsonSerializer.Serialize(first.ExecutableRoot), JsonSerializer.Serialize(second.ExecutableRoot));
        Assert.Equal(["child", "root"], first.DirectDependencies.Select(x => x.OccurrenceId));
        Assert.Same(childDependency, first.DirectDependencies[0]);
        Assert.Same(rootDependency, first.DirectDependencies[1]);
        Assert.Equal("child-origin", first.DirectDependencies[0].NodeOrigin[0].Id);
        Assert.Equal("root-origin", first.DirectDependencies[1].NodeOrigin[0].Id);
        Assert.Equal(new RuntimeRequirement("elsa.graph-activity", "1"), Assert.Single(first.RuntimeRequirements));
        Assert.Equal("elsa.graph-activity", first.ExecutableRoot!.ActivityType);
        Assert.Equal("1", first.ExecutableRoot.ActivityTypeVersion);
        Assert.Equal(WellKnownRuntimeActivityConsumers.GraphActivity, first.ExecutableRoot!.Descriptor.ConsumerKey);
        Assert.True(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("occurrences", out var occurrences));
        Assert.Equal(2, occurrences.GetArrayLength());
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("rootActivity", out _));
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("provider", out _));
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("templateHash", out _));
        Assert.Equal(2, first.ResourceMeasurements.LocalNodeCount);
        Assert.Equal(2, first.ResourceMeasurements.MaximumObservedAuthoredDepth);
        Assert.Equal(2, first.ResourceMeasurements.DependencyCount);
        Assert.Equal("provider-fingerprint", first.ProviderFingerprint);
    }

    [Fact]
    public async Task Dependency_discovery_preserves_parent_slot_and_authored_order()
    {
        var manifest = CreateManifest("""
            {
              "variables": [],
              "outputMappings": [{ "outputReferenceKey": "total", "source": { "syntax": "Literal", "value": 42 } }],
              "rootActivity": {
                "nodeId": "if", "activityVersionId": "version-if", "inputs": [], "outputs": [],
                "structure": { "kind": "If", "schemaVersion": "1", "payload": {
                  "then": { "nodeId": "then", "activityVersionId": "version-then", "inputs": [], "outputs": [], "structure": null },
                  "else": { "nodeId": "else", "activityVersionId": "version-else", "inputs": [], "outputs": [], "structure": null }
                } }
              }
            }
            """);

        var discovery = await _provider.DiscoverDependenciesAsync(new("definition", "draft", 1, manifest));

        Assert.Empty(discovery.Diagnostics);
        var root = Assert.Single(discovery.Dependencies, x => x.OccurrenceId == "if");
        Assert.Null(root.ParentOccurrenceId);
        Assert.Equal("activity-graph", root.ChildSlotName);
        Assert.Equal(("if", "If.Then", 0), discovery.Dependencies.Where(x => x.OccurrenceId == "then").Select(x => (x.ParentOccurrenceId, x.ChildSlotName, x.ChildIndex)).Single());
        Assert.Equal(("if", "If.Else", 0), discovery.Dependencies.Where(x => x.OccurrenceId == "else").Select(x => (x.ParentOccurrenceId, x.ChildSlotName, x.ChildIndex)).Single());
    }

    [Fact]
    public async Task Provider_owned_reference_rewriter_updates_exact_nested_occurrence_and_preserves_structure()
    {
        var service = new TestActivityStructureService();
        var rewriter = new GraphActivityProviderReferenceRewriter(service);
        var manifest = CreateManifest("""
            {
              "variables": [],
              "outputMappings": [],
              "rootActivity": {
                "nodeId": "sequence", "activityVersionId": "version-sequence", "inputs": [], "outputs": [],
                "structure": { "kind": "Sequence", "schemaVersion": "1", "payload": { "activities": [
                  { "nodeId": "target", "activityVersionId": "old", "inputs": [], "outputs": [], "structure": null },
                  { "nodeId": "unchanged", "activityVersionId": "same", "inputs": [], "outputs": [], "structure": null }
                ] } }
              }
            }
            """);

        var rewritten = await rewriter.RewriteReferencesAsync(manifest, [new("target", "old", "new")]);
        Assert.True(ActivityGraphManifest.TryParse(rewritten.Payload, out var graph, out var errors));
        Assert.Empty(errors);
        var root = graph!.RootActivity.Deserialize<ActivityNode>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var children = Assert.Single(service.ProjectChildren(root)).Activities.ToArray();
        Assert.Equal("new", children[0].ActivityVersionId);
        Assert.Equal("same", children[1].ActivityVersionId);
        Assert.Equal("version-sequence", root.ActivityVersionId);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await rewriter.RewriteReferencesAsync(manifest, [new("target", "stale", "new")]));
    }

    [Fact]
    public async Task Compilation_rejects_an_unresolved_or_differently_pinned_occurrence()
    {
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "1.0.0",
            CreateContract(),
            CreateManifest(),
            [CreateDependency("graph-root", "a-different-version", "root-origin")],
            "fingerprint");

        var result = await _provider.CompileAsync(request);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("activity.dependency.version-mismatch", diagnostic.Code);
        Assert.Equal("graph-root", diagnostic.Location!.ReferenceKey);
        Assert.Empty(result.DirectDependencies);
        Assert.Empty(result.RuntimeRequirements);
    }

    [Fact]
    public async Task Same_schema_migration_is_canonical_and_unsupported_migration_is_explicit()
    {
        var source = CreateManifest();

        var first = await _provider.MigrateAsync(new(source, "1"));
        var second = await _provider.MigrateAsync(new(source, "1"));
        var unsupported = await _provider.MigrateAsync(new(source, "2"));

        Assert.NotNull(first.Manifest);
        Assert.Equal(first.Manifest!.Payload.GetRawText(), second.Manifest!.Payload.GetRawText());
        Assert.Empty(first.Diagnostics);
        Assert.Null(unsupported.Manifest);
        Assert.Equal("activity.provider.migration-unsupported", Assert.Single(unsupported.Diagnostics).Code);
    }

    [Fact]
    public async Task Registry_rejects_duplicate_owners_and_wraps_provider_failures()
    {
        var registry = new ActivityProviderRegistry([new ThrowingProvider()]);

        Assert.Throws<InvalidOperationException>(() => registry.Add(new ThrowingProvider()));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("missing", "1"));
        var diagnostics = await registry.Resolve(ThrowingProvider.Key, "1").ValidateAsync(CreateManifest(), CreateContract());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("activity.provider.failure", diagnostic.Code);
        Assert.DoesNotContain("secret infrastructure detail", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("validate", diagnostic.Metadata!["operation"]);
    }

    [Fact]
    public void Diagnostic_ordering_follows_the_public_contract()
    {
        var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-1");
        var diagnostics = new[]
        {
            new ActivityDiagnostic("z.warning", ActivityDiagnosticSeverity.Warning, "warning", subject),
            new ActivityDiagnostic("b.error", ActivityDiagnosticSeverity.Error, "error", subject),
            new ActivityDiagnostic("a.info", ActivityDiagnosticSeverity.Info, "info", subject),
            new ActivityDiagnostic("a.error", ActivityDiagnosticSeverity.Error, "error", subject)
        };

        Assert.Equal(
            ["a.error", "b.error", "z.warning", "a.info"],
            ActivityDiagnosticOrderer.Order(diagnostics).Select(x => x.Code));
    }

    private static ActivityProviderManifest CreateManifest(string? json = null) => new(
        GraphActivityProvider.Key,
        ActivityGraphManifest.SchemaVersion,
        ParseJson(json ?? """
            {
              "variables": [],
              "rootActivity": {
                "structure": null,
                "outputs": [],
                "nodeId": "graph-root",
                "inputs": [],
                "activityVersionId": "activity-ver-root"
              },
              "outputMappings": [{
                "source": { "value": 42, "syntax": "Literal" },
                "outputReferenceKey": "total"
              }]
            }
            """));

    private static ActivityContract CreateContract() => new(
        "1",
        [],
        [new ActivityOutputContract(
            "total",
            "Total",
            new TypeReference("Int64"),
            true,
            "elsa.json")],
        [new ActivityOutcomeContract("done", "Done", true)]);

    private static ActivityResolvedDependency CreateDependency(string occurrenceId, string versionId, string originId) => new(
        $"definition-{occurrenceId}",
        versionId,
        "1.0.0",
        $"template-{occurrenceId}",
        $"sha256-{occurrenceId}",
        CreateContract(),
        ActivityDefinitionVersionLifecycle.Active,
        null,
        occurrenceId,
        [new ActivityNodeOrigin("AuthoredNode", originId)]);

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(Encoding.UTF8.GetBytes(json));
        return document.RootElement.Clone();
    }

    private sealed class ThrowingProvider : IActivityProvider
    {
        public const string Key = "test.throwing";
        public string ProviderKey => Key;
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Throwing Provider",
            [new("1", true, new HashSet<string> { "1" })],
            new([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret infrastructure detail");

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret infrastructure detail");

        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret infrastructure detail");

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret infrastructure detail");
    }

    private sealed class TestContractCapabilityCatalog : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; } =
        [
            Capability("Decimal"),
            Capability("Int64")
        ];

        private static ActivityContractTypeCapability Capability(string alias) => new(
            alias,
            alias,
            "Test",
            "Test",
            Enum.GetValues<CollectionKind>().ToHashSet(),
            false,
            true,
            new HashSet<string>(StringComparer.Ordinal) { "elsa.json" });
    }

    private sealed class TestActivityStructureService : IActivityStructureService
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            if (activity.Structure is null)
                return [];
            var payload = activity.Structure.Payload;
            if (StringComparer.Ordinal.Equals(activity.Structure.Kind, "Sequence") && payload.TryGetProperty("activities", out var activities))
                return [new("Sequence.Activities", activities.Deserialize<ActivityNode[]>(Options) ?? [])];
            if (StringComparer.Ordinal.Equals(activity.Structure.Kind, "If"))
            {
                var then = payload.TryGetProperty("then", out var thenElement) && thenElement.ValueKind == JsonValueKind.Object
                    ? thenElement.Deserialize<ActivityNode>(Options)
                    : null;
                var @else = payload.TryGetProperty("else", out var elseElement) && elseElement.ValueKind == JsonValueKind.Object
                    ? elseElement.Deserialize<ActivityNode>(Options)
                    : null;
                return [
                    new("If.Then", then is null ? [] : [then]),
                    new("If.Else", @else is null ? [] : [@else])
                ];
            }
            return [];
        }

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
        {
            if (activity.Structure is null)
                return activity;
            var payload = JsonNode.Parse(activity.Structure.Payload.GetRawText())!.AsObject();
            if (StringComparer.Ordinal.Equals(activity.Structure.Kind, "Sequence"))
                payload["activities"] = JsonSerializer.SerializeToNode(childProjections.Single().Activities, Options);
            else if (StringComparer.Ordinal.Equals(activity.Structure.Kind, "If"))
            {
                payload["then"] = JsonSerializer.SerializeToNode(childProjections.Single(x => x.Name == "If.Then").Activities.SingleOrDefault(), Options);
                payload["else"] = JsonSerializer.SerializeToNode(childProjections.Single(x => x.Name == "If.Else").Activities.SingleOrDefault(), Options);
            }
            return activity with { Structure = new(activity.Structure.Kind, activity.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Options)) };
        }
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}
