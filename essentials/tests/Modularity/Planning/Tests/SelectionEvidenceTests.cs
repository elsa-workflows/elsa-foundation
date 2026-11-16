using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;
using Xunit;

namespace Elsa.Modularity.Planning.Tests;

public sealed class SelectionEvidenceTests
{
    [Fact]
    public void Reviewed_and_loaded_edges_remain_distinct_without_changing_exact_selection()
    {
        var group = PlannerFixture.Definition("group", "base", ["A", "B"],
            explanations: [new DependencyExplanation("A", "C", "optional", "Reviewed companion")]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var authored = PlannerFixture.Authored(catalog, groups: [PlannerFixture.Ref(group)], remove: ["B"]);
        var inventory = PlannerFixture.Inventory(
            PlannerFixture.Bundled("A", runtimeDependencies: ["B"], manifestDependencies: [new InventoryDependency("C", true)]));

        var plan = SelectionPlanner.Plan(catalog, authored, inventory);

        Assert.Equal(["A"], plan.SelectedFeatureIds.ToArray());
        Assert.Contains(plan.DependencyEvidence, edge => edge is { FeatureId: "A", DependencyId: "B", Mode: "required", EvidenceKind: "runtime-descriptor", EvidenceSource: "runtime-descriptor", TargetSelected: false });
        Assert.Contains(plan.DependencyEvidence, edge => edge is { FeatureId: "A", DependencyId: "C", Mode: "optional", EvidenceKind: "reviewed-definition", EvidenceSource: "base", TargetSelected: false });
        Assert.DoesNotContain(plan.DependencyEvidence, edge => edge.EvidenceKind == "package-manifest");
        Assert.Contains(plan.Findings, finding => finding.Code == "required-dependency-missing" && finding.DependencyId == "B");
    }

    [Fact]
    public void Manifest_edge_is_used_only_without_a_loaded_descriptor()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog, add: ["A"]);
        var manifestOnly = PlannerFixture.Inventory(PlannerFixture.Packaged("A", manifestDependencies: [new InventoryDependency("C", true)]));
        var withDescriptor = PlannerFixture.Inventory(PlannerFixture.Bundled("A", runtimeDependencies: [], manifestDependencies: [new InventoryDependency("C", true)]));

        var plan = SelectionPlanner.Plan(catalog, authored, manifestOnly);
        var loadedPlan = SelectionPlanner.Plan(catalog, authored, withDescriptor);

        Assert.Contains(plan.DependencyEvidence, edge => edge is { DependencyId: "C", Mode: "optional", EvidenceKind: "package-manifest", EvidenceSource: "installed-manifest", TargetSelected: false });
        Assert.Empty(loadedPlan.DependencyEvidence);
        Assert.Contains(loadedPlan.Findings, finding => finding.Code == "descriptor-manifest-divergence");
    }

    [Fact]
    public void Dependency_evidence_order_does_not_follow_declaration_order()
    {
        var group = PlannerFixture.Definition("group", "base", ["B", "A"], explanations:
        [
            new DependencyExplanation("A", "Z", "optional", "Later edge"),
            new DependencyExplanation("A", "C", "optional", "Earlier edge")
        ]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var authored = PlannerFixture.Authored(catalog, groups: [PlannerFixture.Ref(group)]);
        var inventory = PlannerFixture.Inventory(PlannerFixture.Bundled("B", ["A"]), PlannerFixture.Bundled("A", ["B"]));

        var edges = SelectionPlanner.Plan(catalog, authored, inventory).DependencyEvidence
            .Select(edge => $"{edge.FeatureId}->{edge.DependencyId}:{edge.EvidenceKind}")
            .ToArray();

        Assert.Equal(["A->B:runtime-descriptor", "A->C:reviewed-definition", "A->Z:reviewed-definition", "B->A:runtime-descriptor"], edges);
    }

    [Fact]
    public void Inventory_reader_preserves_empty_vs_absent_descriptor_and_resource_hints_stay_unchecked()
    {
        const string inventory = """
            {"schemaVersion":"1","inventoryId":"snapshot-42","targetId":"staging","observedAt":"2026-09-25T09:00:00Z","source":"supplied-snapshot","features":[
              {"featureId":"A","availability":"loaded","runtimeDependencies":[],"manifestDependencies":null,"manifestReadStatus":"absent","package":null,"hostBundled":true,"compatibility":"unknown","evidenceSource":"target-export"},
              {"featureId":"B","availability":"installed","runtimeDependencies":null,"manifestDependencies":[],"manifestReadStatus":"read","package":{"packageId":"Elsa.B","packageVersion":"1.0.0","manifestDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"hostBundled":false,"compatibility":"compatible","evidenceSource":"target-export"}
            ]}
            """;
        const string resourceHints = """{"schemaVersion":"1","source":"developer-file","resourceReferences":["primary"]}""";

        var parsed = SelectionEvidenceJsonReader.ParseInventory(inventory);
        var persistence = SelectionEvidenceJsonReader.ParseResourceHints(resourceHints);

        Assert.Empty(parsed.Features[0].RuntimeDependencies!.Value);
        Assert.Null(parsed.Features[1].RuntimeDependencies);
        Assert.Equal("unchecked", persistence.Status);
        Assert.Equal(["primary"], persistence.ResourceReferences.ToArray());
        Assert.Equal("supplied-file", persistence.Provenance);
    }

    [Theory]
    [InlineData("""{"schemaVersion":"1","source":"developer-file","resourceReferences":["primary"],"status":"checked"}""")]
    [InlineData("""{"schemaVersion":"1","source":"developer-file","resourceReferences":["Server=db;Password=secret"]}""")]
    [InlineData("""{"schemaVersion":"1","source":"developer-file","resourceReferences":["primary","primary"]}""")]
    public void Resource_hint_reader_refuses_status_secrets_and_duplicates(string json) =>
        Assert.Throws<SelectionDocumentException>(() => SelectionEvidenceJsonReader.ParseResourceHints(json));

    [Theory]
    [InlineData("""{"schemaVersion":"1","inventoryId":"i","targetId":"t","observedAt":"2026-09-25T09:00:00","source":"file","features":[]}""")]
    [InlineData("""{"schemaVersion":"1","inventoryId":"i","targetId":"t","observedAt":"2026-09-25T09:00:00Z","source":"file","features":[],"features":[]}""")]
    [InlineData("""{"schemaVersion":"1","inventoryId":"i","targetId":"t","observedAt":"2026-09-25T09:00:00Z","source":"file","features":[],"unknown":true}""")]
    public void Inventory_reader_refuses_missing_offset_duplicate_key_and_unknown_field(string json) =>
        Assert.Throws<SelectionDocumentException>(() => SelectionEvidenceJsonReader.ParseInventory(json));
}
