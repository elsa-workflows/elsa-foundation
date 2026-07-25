using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

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
        var (rootActivity, variables, outputMappings) = firstManifest;
        Assert.Equal("graph-root", rootActivity.GetProperty("nodeId").GetString());
        Assert.Single(variables);
        Assert.Single(outputMappings);
        Assert.Equal(firstManifest.ToCanonicalUtf8Bytes(), secondManifest!.ToCanonicalUtf8Bytes());
    }

    [Fact]
    public void Schema_two_round_trip_preserves_outcome_mappings()
    {
        var payload = ParseJson("""
            {
              "variables": [],
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": null
              },
              "outputMappings": [],
              "outcomeMappings": [
                {
                  "sourceOutcomeReferenceKey": "rejected",
                  "boundaryOutcomeReferenceKey": "declined"
                },
                {
                  "sourceOutcomeReferenceKey": "approved",
                  "boundaryOutcomeReferenceKey": "accepted"
                }
              ]
            }
            """);

        Assert.True(ActivityGraphManifest.TryParse(ActivityGraphManifest.MultipleOutcomesSchemaVersion, payload, out var manifest, out var errors));

        Assert.Empty(errors);
        Assert.Equal(ActivityGraphManifest.MultipleOutcomesSchemaVersion, manifest!.ManifestSchemaVersion);
        Assert.Equal(
            [
                new ActivityGraphOutcomeMapping("rejected", "declined"),
                new ActivityGraphOutcomeMapping("approved", "accepted")
            ],
            manifest.OutcomeMappings);
        Assert.True(ActivityGraphManifest.TryParse(
            ActivityGraphManifest.MultipleOutcomesSchemaVersion,
            manifest.ToCanonicalJsonElement(),
            out var canonical,
            out var canonicalErrors));
        Assert.Empty(canonicalErrors);
        Assert.Equal(manifest.ToCanonicalUtf8Bytes(), canonical!.ToCanonicalUtf8Bytes());
    }

    [Fact]
    public async Task Contract_proposal_does_not_duplicate_public_contract_and_seeds_done()
    {
        var proposal = await _provider.ProposeContractAsync(new(CreateManifest(), new DesignActivityContract("1", [], [], [])));

        Assert.Empty(proposal.Diagnostics);
        var change = Assert.Single(proposal.Changes);
        Assert.Equal(ActivityContractProposalOperation.Add, change.Operation);
        var outcome = Assert.IsType<ActivityOutcomeContract>(change.Outcome);
        Assert.Equal("done", outcome.ReferenceKey);
        Assert.True(outcome.IsEmitted);
        Assert.Empty(_provider.AuthoringCapabilities.ContractConstraints.RequiredOutcomes);
    }

    [Fact]
    public async Task Schema_two_contract_proposal_preserves_authored_outcomes()
    {
        var contract = OutcomeContract(("accepted", "Accepted"), ("declined", "Declined"));

        var proposal = await _provider.ProposeContractAsync(new(CreateSchemaTwoManifest(), contract));

        Assert.Empty(proposal.Diagnostics);
        Assert.Empty(proposal.Changes);
        Assert.Equal(
            [ActivityGraphManifest.SchemaVersion, ActivityGraphManifest.MultipleOutcomesSchemaVersion],
            _provider.SupportedManifestSchemas.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Schema_two_requires_at_least_one_emitted_boundary_outcome()
    {
        var diagnostics = await _provider.ValidateAsync(
            CreateSchemaTwoManifest("[]"),
            new DesignActivityContract("1", [], [], []));

        Assert.Contains(diagnostics, x => x.Code == "activity.graph.outcome-required");
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
    public async Task Validation_does_not_treat_flowchart_connection_endpoints_as_activity_nodes()
    {
        var manifest = CreateManifest("""
            {
              "rootActivity": {
                "nodeId": "flowchart",
                "activityVersionId": "activity-ver-flowchart",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "elsa.flowchart.structure",
                  "schemaVersion": "1.0.0",
                  "payload": {
                    "activities": [
                      { "nodeId": "first", "activityVersionId": "activity-ver-first", "inputs": [], "outputs": [], "structure": null },
                      { "nodeId": "second", "activityVersionId": "activity-ver-second", "inputs": [], "outputs": [], "structure": null }
                    ],
                    "connections": [{
                      "source": { "nodeId": "first", "port": "Done" },
                      "target": { "nodeId": "second", "port": null }
                    }]
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

        Assert.Empty(diagnostics);
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
                "inputs": [{
                  "referenceKey": "condition",
                  "value": { "value": true, "expressionType": "Literal" }
                }],
                "activityVersionId": "activity-ver-root",
                "nodeId": "root"
              }
            }
            """);
        var rootContract = CreateContract() with
        {
            Inputs =
            [
                new Elsa.Activities.Design.Core.Models.ActivityInputContract(
                    "condition",
                    "Condition",
                    new TypeReference("Boolean"),
                    true,
                    false,
                    null,
                    "elsa.json")
            ]
        };
        var rootDependency = CreateDependency(
            "root",
            "activity-ver-root",
            "root-origin",
            rootContract,
            declaredStructure: new("Sequence", "1"));
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
        var runtimeContract = Assert.IsType<Elsa.Activities.Runtime.Core.Models.ActivityContract>(first.ExecutableRoot.ActivityContract);
        Assert.Equal("activity-type-1", runtimeContract.ActivityTypeKey);
        Assert.Equal("2.0.0", runtimeContract.ContractVersion);
        Assert.Equal(WellKnownRuntimeActivityConsumers.GraphActivity, runtimeContract.DescriptorKind);
        Assert.True(JsonElement.DeepEquals(first.ExecutableRoot.Descriptor.Payload, runtimeContract.DescriptorPayload));
        Assert.Equal([ActivityOutcomes.Done], runtimeContract.Outcomes);
        var totalProjection = Assert.Single(runtimeContract.Result.Projections);
        Assert.Equal("total", totalProjection.Key);
        Assert.Equal("total", totalProjection.Value.Path);
        Assert.True(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("occurrences", out var occurrences));
        Assert.Equal(2, occurrences.GetArrayLength());
        var rootOccurrence = first.Occurrences.Single(x => x.OccurrenceId == "root");
        Assert.Equal("condition", Assert.Single(rootOccurrence.InputBindings.EnumerateArray()).GetProperty("referenceKey").GetString());
        Assert.NotNull(rootOccurrence.Structure);
        Assert.Equal("Sequence", rootOccurrence.Structure.Kind);
        Assert.Equal("child", Assert.Single(rootOccurrence.Structure.Payload.GetProperty("activities").EnumerateArray()).GetString());
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("rootActivity", out _));
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("provider", out _));
        Assert.False(first.ExecutableRoot.Descriptor.Payload.TryGetProperty("templateHash", out _));
        Assert.Equal(2, first.ResourceMeasurements.LocalNodeCount);
        Assert.Equal(2, first.ResourceMeasurements.MaximumObservedAuthoredDepth);
        Assert.Equal(2, first.ResourceMeasurements.DependencyCount);
        Assert.Equal("provider-fingerprint", first.ProviderFingerprint);
    }

    [Fact]
    public async Task Schema_two_compilation_resolves_multiple_boundary_outcomes()
    {
        var boundaryContract = OutcomeContract(("accepted", "Accepted"), ("declined", "Declined"));
        var entryContract = OutcomeContract(("approved", "Approved"), ("rejected", "Rejected"));
        var manifest = CreateSchemaTwoManifest();
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "2.0.0",
            boundaryContract,
            manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin", entryContract)],
            "provider-fingerprint");

        var compilation = await _provider.CompileAsync(request);

        Assert.Empty(compilation.Diagnostics);
        var payload = compilation.ExecutableRoot!.Descriptor.Payload;
        Assert.Equal(
            new[]
            {
                new GraphActivityOutcomeMappingDescriptor("Approved", "Accepted"),
                new GraphActivityOutcomeMappingDescriptor("Rejected", "Declined")
            },
            payload.GetProperty("outcomeMappings").Deserialize<GraphActivityOutcomeMappingDescriptor[]>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["Accepted", "Declined"], compilation.ExecutableRoot.ActivityContract!.Outcomes);
    }

    [Theory]
    [InlineData(
        """[{"sourceOutcomeReferenceKey":"approved","boundaryOutcomeReferenceKey":"accepted"}]""",
        "activity.graph.outcome-mapping-required")]
    [InlineData(
        """[{"sourceOutcomeReferenceKey":"approved","boundaryOutcomeReferenceKey":"accepted"},{"sourceOutcomeReferenceKey":"approved","boundaryOutcomeReferenceKey":"declined"}]""",
        "activity.graph.outcome-mapping-source-duplicate")]
    [InlineData(
        """[{"sourceOutcomeReferenceKey":"approved","boundaryOutcomeReferenceKey":"accepted"},{"sourceOutcomeReferenceKey":"rejected","boundaryOutcomeReferenceKey":"accepted"}]""",
        "activity.graph.outcome-mapping-boundary-duplicate")]
    [InlineData(
        """[{"sourceOutcomeReferenceKey":"missing","boundaryOutcomeReferenceKey":"accepted"},{"sourceOutcomeReferenceKey":"rejected","boundaryOutcomeReferenceKey":"declined"}]""",
        "activity.graph.outcome-mapping-source-unknown")]
    [InlineData(
        """[{"sourceOutcomeReferenceKey":"approved","boundaryOutcomeReferenceKey":"missing"},{"sourceOutcomeReferenceKey":"rejected","boundaryOutcomeReferenceKey":"declined"}]""",
        "activity.graph.outcome-mapping-boundary-unknown")]
    public async Task Schema_two_compilation_rejects_invalid_outcome_mappings(string mappings, string expectedCode)
    {
        var manifest = CreateSchemaTwoManifest(mappings);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "2.0.0",
            OutcomeContract(("accepted", "Accepted"), ("declined", "Declined")),
            manifest,
            [CreateDependency(
                "graph-root",
                "activity-ver-root",
                "root-origin",
                OutcomeContract(("approved", "Approved"), ("rejected", "Rejected")))],
            "provider-fingerprint");

        var compilation = await _provider.CompileAsync(request);

        Assert.Contains(compilation.Diagnostics, x => x.Code == expectedCode);
        Assert.Null(compilation.ExecutableRoot);
    }

    [Fact]
    public async Task Dependency_discovery_preserves_parent_slot_and_authored_order()
    {
        var manifest = CreateManifest("""
            {
              "variables": [],
              "outputMappings": [{ "outputReferenceKey": "total", "source": { "syntax": "Literal", "value": 42 } }],
              "rootActivity": {
                "nodeId": "if", "activityVersionId": "version-if",
                "inputs": [{ "referenceKey": "customer", "value": { "value": "secret", "expressionType": "Literal" }, "isSensitive": true }],
                "outputs": [{ "referenceKey": "result", "value": { "value": null } }],
                "structure": { "kind": "If", "schemaVersion": "1", "payload": {
                  "then": { "nodeId": "then", "activityVersionId": "version-then", "inputs": [], "outputs": [], "structure": null },
                  "else": { "nodeId": "else", "activityVersionId": "version-else", "inputs": [], "outputs": [], "structure": null },
                  "outcomeUsage": [{ "nodeId": "then", "referenceKey": "approved" }]
                } }
              }
            }
            """);

        var discovery = await _provider.DiscoverDependenciesAsync(new("definition", "draft", 1, manifest));

        Assert.Empty(discovery.Diagnostics);
        var root = Assert.Single(discovery.Dependencies, x => x.OccurrenceId == "if");
        Assert.Null(root.ParentOccurrenceId);
        Assert.Equal("activity-graph", root.ChildSlotName);
        Assert.Equal(
            [new("Input", "customer", "Bound"), new("Output", "result", "Bound")],
            root.MemberUsage);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(root.MemberUsage), StringComparison.Ordinal);
        var then = Assert.Single(discovery.Dependencies, x => x.OccurrenceId == "then");
        Assert.Equal(("if", "If.Then", 0), (then.ParentOccurrenceId, then.ChildSlotName, then.ChildIndex));
        Assert.Equal([new("Outcome", "approved", "Connected")], then.MemberUsage);
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
            CreateManifest("""
                {
                  "variables": [],
                  "rootActivity": {
                    "nodeId": "graph-root",
                    "activityVersionId": "activity-ver-root",
                    "inputs": [],
                    "outputs": [],
                    "structure": {
                      "kind": "Sequence",
                      "schemaVersion": "1",
                      "payload": { "activities": [] }
                    }
                  },
                  "outputMappings": [{
                    "source": { "value": 42, "syntax": "Literal" },
                    "outputReferenceKey": "total"
                  }]
                }
                """),
            [CreateDependency(
                "graph-root",
                "a-different-version",
                "root-origin",
                declaredStructure: new("Sequence", "1"))],
            "fingerprint");

        var result = await _provider.CompileAsync(request);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("activity.dependency.version-mismatch", diagnostic.Code);
        Assert.Equal("graph-root", diagnostic.Location!.ReferenceKey);
        Assert.Empty(result.DirectDependencies);
        Assert.Empty(result.RuntimeRequirements);
    }

    [Theory]
    [InlineData("Sequence", "1")]
    [InlineData("elsa.sequence.structure", "1")]
    public async Task Compilation_rejects_a_structure_identity_that_differs_from_the_exact_activity_version(
        string actualKind,
        string actualSchemaVersion)
    {
        var manifest = CreateManifest($$"""
            {
              "variables": [],
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "{{actualKind}}",
                  "schemaVersion": "{{actualSchemaVersion}}",
                  "payload": { "activities": [] }
                }
              },
              "outputMappings": [{
                "source": { "value": 42, "syntax": "Literal" },
                "outputReferenceKey": "total"
              }]
            }
            """);
        var dependency = CreateDependency(
            "graph-root",
            "activity-ver-root",
            "root-origin",
            declaredStructure: new("elsa.sequence.structure", "1.0.0"));
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "1.0.0",
            CreateContract(),
            manifest,
            [dependency],
            "fingerprint");

        var result = await _provider.CompileAsync(request);

        Assert.Null(result.ExecutableRoot);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("activity.graph.structure-contract-mismatch", diagnostic.Code);
        Assert.Equal("/rootActivity/structure", diagnostic.Location!.JsonPointer);
        Assert.Equal("elsa.sequence.structure", diagnostic.Metadata!["declaredKind"]);
        Assert.Equal("1.0.0", diagnostic.Metadata["declaredSchemaVersion"]);
        Assert.Equal(actualKind, diagnostic.Metadata["actualKind"]);
        Assert.Equal(actualSchemaVersion, diagnostic.Metadata["actualSchemaVersion"]);
    }

    [Fact]
    public async Task Compilation_preserves_unknown_opaque_structure_when_the_activity_declares_no_structure_contract()
    {
        var manifest = CreateManifest("""
            {
              "variables": [],
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "vendor.opaque",
                  "schemaVersion": "1",
                  "payload": {
                    "metadata": {
                      "nodeId": "not-an-authored-child"
                    }
                  }
                }
              },
              "outputMappings": [{
                "source": { "value": 42, "syntax": "Literal" },
                "outputReferenceKey": "total"
              }]
            }
            """);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "1.0.0",
            CreateContract(),
            manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin")],
            "fingerprint");

        var result = await _provider.CompileAsync(request);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.ExecutableRoot);
    }

    [Fact]
    public async Task Compilation_rejects_a_handled_structure_that_the_exact_activity_version_does_not_declare()
    {
        var manifest = CreateManifest("""
            {
              "variables": [],
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "elsa.sequence.structure",
                  "schemaVersion": "1.0.0",
                  "payload": { "activities": [] }
                }
              },
              "outputMappings": [{
                "source": { "value": 42, "syntax": "Literal" },
                "outputReferenceKey": "total"
              }]
            }
            """);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "1.0.0",
            CreateContract(),
            manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin")],
            "fingerprint");

        var result = await _provider.CompileAsync(request);

        Assert.Null(result.ExecutableRoot);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("activity.graph.structure-contract-undeclared", diagnostic.Code);
        Assert.Equal("/rootActivity/structure", diagnostic.Location!.JsonPointer);
    }

    [Fact]
    public async Task Compilation_rejects_a_declared_structure_when_its_handler_is_unavailable()
    {
        var provider = new GraphActivityProvider(
            new TestActivityStructureService(hasHandlers: false),
            new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()));
        var manifest = CreateManifest("""
            {
              "variables": [],
              "rootActivity": {
                "nodeId": "graph-root",
                "activityVersionId": "activity-ver-root",
                "inputs": [],
                "outputs": [],
                "structure": {
                  "kind": "elsa.sequence.structure",
                  "schemaVersion": "1.0.0",
                  "payload": { "activities": [] }
                }
              },
              "outputMappings": [{
                "source": { "value": 42, "syntax": "Literal" },
                "outputReferenceKey": "total"
              }]
            }
            """);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            7,
            "1.0.0",
            CreateContract(),
            manifest,
            [CreateDependency(
                "graph-root",
                "activity-ver-root",
                "root-origin",
                declaredStructure: new("elsa.sequence.structure", "1.0.0"))],
            "fingerprint");

        var matchingResult = await _provider.CompileAsync(request);
        var result = await provider.CompileAsync(request);

        Assert.Empty(matchingResult.Diagnostics);
        Assert.NotNull(matchingResult.ExecutableRoot);
        Assert.Null(result.ExecutableRoot);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("activity.graph.structure-handler-unavailable", diagnostic.Code);
        Assert.Equal("/rootActivity/structure", diagnostic.Location!.JsonPointer);
    }

    [Fact]
    public async Task Same_schema_and_schema_two_migrations_are_canonical_and_unsupported_migration_is_explicit()
    {
        var source = CreateManifest();

        var first = await _provider.MigrateAsync(new(source, "1"));
        var second = await _provider.MigrateAsync(new(source, "1"));
        var schemaTwo = await _provider.MigrateAsync(new(source, "2"));
        var unsupported = await _provider.MigrateAsync(new(source, "99"));

        Assert.NotNull(first.Manifest);
        Assert.Equal(first.Manifest!.Payload.GetRawText(), second.Manifest!.Payload.GetRawText());
        Assert.Empty(first.Diagnostics);
        Assert.NotNull(schemaTwo.Manifest);
        Assert.Equal("2", schemaTwo.Manifest!.SchemaVersion);
        Assert.Equal(JsonValueKind.Array, schemaTwo.Manifest.Payload.GetProperty("outcomeMappings").ValueKind);
        Assert.Equal(0, schemaTwo.Manifest.Payload.GetProperty("outcomeMappings").GetArrayLength());
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

    [Fact]
    public void Structure_facet_reader_requires_the_typed_payload_shape()
    {
        var valid = new ActivityDesignFacet(
            "elsa.sequence.structure",
            "1.0.0",
            JsonSerializer.SerializeToElement(new ActivityStructureDesignFacetPayload(
                "generic",
                true,
                [],
                new Dictionary<string, object?>()),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var unrelated = new ActivityDesignFacet(
            "vendor.options",
            "1",
            JsonSerializer.SerializeToElement(new { slots = Array.Empty<object>() }));
        var malformed = new ActivityDesignFacet(
            "elsa.sequence.structure",
            "1.0.0",
            JsonSerializer.SerializeToElement(new { slots = Array.Empty<object>() }));
        var secondValid = new ActivityDesignFacet(
            "elsa.flowchart.structure",
            "1.0.0",
            valid.Payload);

        Assert.True(ActivityStructureDesignFacetReader.TryReadContract(valid, out var contract));
        Assert.Equal(new ActivityStructureContract("elsa.sequence.structure", "1.0.0"), contract);
        Assert.False(ActivityStructureDesignFacetReader.TryReadContract(unrelated, out _));
        Assert.False(ActivityStructureDesignFacetReader.TryReadContract(malformed, out _));
        Assert.True(ActivityStructureDesignFacetReader.TryReadSingle([unrelated, malformed, valid], out var single));
        Assert.Same(valid, single);
        Assert.False(ActivityStructureDesignFacetReader.TryReadSingle([valid, secondValid], out _));
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

    private static ActivityProviderManifest CreateSchemaTwoManifest(string? outcomeMappings = null) => new(
        GraphActivityProvider.Key,
        ActivityGraphManifest.MultipleOutcomesSchemaVersion,
        ParseJson($$"""
            {
              "variables": [],
              "rootActivity": {
                "structure": null,
                "outputs": [],
                "nodeId": "graph-root",
                "inputs": [],
                "activityVersionId": "activity-ver-root"
              },
              "outputMappings": [],
              "outcomeMappings": {{outcomeMappings ?? """
                [
                  {
                    "sourceOutcomeReferenceKey": "approved",
                    "boundaryOutcomeReferenceKey": "accepted"
                  },
                  {
                    "sourceOutcomeReferenceKey": "rejected",
                    "boundaryOutcomeReferenceKey": "declined"
                  }
                ]
                """}}
            }
            """));

    private static DesignActivityContract CreateContract() => new(
        "1",
        [],
        [new ActivityOutputContract(
            "total",
            "Total",
            new TypeReference("Int64"),
            true,
            false,
            "elsa.json")],
        [new ActivityOutcomeContract("done", "Done", true)]);

    private static DesignActivityContract OutcomeContract(params (string ReferenceKey, string Name)[] outcomes) => new(
        "1",
        [],
        [],
        outcomes.Select(x => new ActivityOutcomeContract(x.ReferenceKey, x.Name, true)).ToArray());

    private static ActivityResolvedDependency CreateDependency(
        string occurrenceId,
        string versionId,
        string originId,
        DesignActivityContract? contract = null,
        ActivityStructureContract? declaredStructure = null) => new(
        $"definition-{occurrenceId}",
        versionId,
        "1.0.0",
        $"template-{occurrenceId}",
        $"sha256-{occurrenceId}",
        contract ?? CreateContract(),
        ActivityDefinitionVersionLifecycle.Active,
        null,
        occurrenceId,
        [new ActivityNodeOrigin("AuthoredNode", originId)],
        DeclaredStructure: declaredStructure);

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

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, DesignActivityContract contract, CancellationToken cancellationToken = default) =>
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

    private sealed class TestActivityStructureService(bool hasHandlers = true) : IActivityStructureService
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            if (activity.Structure is null)
                return [];
            var payload = activity.Structure.Payload;
            if (IsSequence(activity.Structure) && payload.TryGetProperty("activities", out var activities))
                return [new("Sequence.Activities", activities.Deserialize<ActivityNode[]>(Options) ?? [])];
            if (IsFlowchart(activity.Structure) && payload.TryGetProperty("activities", out activities))
                return [new("Flowchart.Activities", activities.Deserialize<ActivityNode[]>(Options) ?? [])];
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
            if (IsSequence(activity.Structure))
                payload["activities"] = JsonSerializer.SerializeToNode(childProjections.Single().Activities, Options);
            else if (StringComparer.Ordinal.Equals(activity.Structure.Kind, "If"))
            {
                payload["then"] = JsonSerializer.SerializeToNode(childProjections.Single(x => x.Name == "If.Then").Activities.SingleOrDefault(), Options);
                payload["else"] = JsonSerializer.SerializeToNode(childProjections.Single(x => x.Name == "If.Else").Activities.SingleOrDefault(), Options);
            }
            return activity with { Structure = new(activity.Structure.Kind, activity.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Options)) };
        }
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity)
        {
            if (activity.Structure is null)
                return null;
            if (!IsSequence(activity.Structure))
                return activity.Structure;

            var childIds = ProjectChildren(activity)
                .SelectMany(x => x.Activities)
                .Select(x => x.NodeId)
                .ToArray();
            return new(
                activity.Structure.Kind,
                activity.Structure.SchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = childIds }, Options));
        }
        public bool HasHandler(ActivityNodeStructure structure) =>
            hasHandlers &&
            (IsSequence(structure) ||
             IsFlowchart(structure) ||
             StringComparer.Ordinal.Equals(structure.Kind, "If") &&
             StringComparer.Ordinal.Equals(structure.SchemaVersion, "1"));
        public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity)
        {
            if (activity.Structure?.Payload.TryGetProperty("outcomeUsage", out var usage) != true)
                return [];
            return usage.EnumerateArray()
                .Select(item => new ActivityChildContractMemberUsage(
                    item.GetProperty("nodeId").GetString()!,
                    [new("Outcome", item.GetProperty("referenceKey").GetString()!, "Connected")]))
                .ToArray();
        }
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;

        private static bool IsSequence(ActivityNodeStructure structure) =>
            (StringComparer.Ordinal.Equals(structure.Kind, "Sequence") &&
             StringComparer.Ordinal.Equals(structure.SchemaVersion, "1")) ||
            (StringComparer.Ordinal.Equals(structure.Kind, "elsa.sequence.structure") &&
             StringComparer.Ordinal.Equals(structure.SchemaVersion, "1.0.0"));

        private static bool IsFlowchart(ActivityNodeStructure structure) =>
            StringComparer.Ordinal.Equals(structure.Kind, "elsa.flowchart.structure") &&
            StringComparer.Ordinal.Equals(structure.SchemaVersion, "1.0.0");
    }
}
