using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Tests;

public sealed class HostAssessmentTests
{
    [Fact]
    public void Removed_required_dependency_stays_absent_and_is_unresolved()
    {
        var catalog = PlannerFixture.Catalog(groups: [PlannerFixture.Definition("group", "g", ["A", "B"])]);
        var plan = SelectionPlanner.Plan(catalog,
            PlannerFixture.Authored(catalog, groups: [PlannerFixture.Ref(catalog.Groups[0])], remove: ["B"], accepted: ["A"]),
            PlannerFixture.Inventory(PlannerFixture.Bundled("A", ["B"])));

        Assert.Equal(["A"], plan.SelectedFeatureIds.ToArray());
        Assert.Contains(plan.Findings, x => x.Code == "required-dependency-missing" && x.FeatureId == "A" && x.DependencyId == "B" && x.Severity == "unresolved");
        Assert.DoesNotContain(plan.SelectedFeatureIds, x => x == "B");
    }

    [Fact]
    public void Manifest_optional_companion_is_advisory_and_not_auto_selected()
    {
        var plan = ForSingle("A", PlannerFixture.Packaged("A", manifestDependencies: [new InventoryDependency("B", true)]));
        Assert.Equal(["A"], plan.SelectedFeatureIds.ToArray());
        Assert.Contains(plan.Findings, x => x.Code == "optional-companion" && x.DependencyId == "B" && x.Severity == "advisory");
        Assert.DoesNotContain(plan.Findings, x => x.Code == "required-dependency-missing");
    }

    [Fact]
    public void Authoritative_empty_runtime_descriptor_overrides_manifest_and_exposes_disagreement()
    {
        var row = PlannerFixture.Bundled("A", [], [new InventoryDependency("B", false)]);
        var plan = ForSingle("A", row);
        Assert.Contains(plan.Findings, x => x.Code == "descriptor-manifest-divergence" && x.Severity == "advisory");
        Assert.DoesNotContain(plan.Findings, x => x.Code == "required-dependency-missing");
        Assert.DoesNotContain(plan.Findings, x => x.Code == "optional-companion");
    }

    [Fact]
    public void Optional_manifest_edge_disagrees_with_required_runtime_edge()
    {
        var row = PlannerFixture.Bundled("A", ["B"], [new InventoryDependency("B", true)]);
        var plan = ForSingle("A", row);
        Assert.Contains(plan.Findings, x => x.Code == "descriptor-manifest-divergence");
        Assert.Contains(plan.Findings, x => x.Code == "required-dependency-missing" && x.DependencyId == "B");
    }

    [Theory]
    [InlineData("absent", "read", "compatible", "package-absent")]
    [InlineData("installed", "unreadable", "compatible", "manifest-unreadable")]
    [InlineData("installed", "read", "incompatible", "package-incompatible")]
    [InlineData("installed", "read", "unknown", "compatibility-unknown")]
    public void Supplied_package_evidence_is_reported_without_installing_anything(string availability, string manifestStatus, string compatibility, string code)
    {
        var plan = ForSingle("A", PlannerFixture.Packaged("A", availability, manifestStatus, compatibility));
        Assert.Contains(plan.Findings, x => x.Code == code && x.FeatureId == "A");
        Assert.Contains(plan.ObservedLocks, x => x.FeatureId == "A" && x.Kind == "package" && x.PackageId == "Elsa.Test.A");
    }

    [Fact]
    public void Unknown_feature_and_missing_inventory_are_explicitly_unresolved()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog, add: ["A"], accepted: ["A"]);
        var unknown = SelectionPlanner.Plan(catalog, authored, PlannerFixture.Inventory());
        var noInventory = SelectionPlanner.Plan(catalog, authored);
        Assert.Contains(unknown.Findings, x => x.Code == "feature-unknown" && x.Severity == "unresolved");
        Assert.Contains(noInventory.Findings, x => x.Code == "inventory-unverified" && x.Severity == "unresolved");
    }

    [Fact]
    public void Duplicate_inventory_rows_and_edges_are_rejected()
    {
        var row = PlannerFixture.Bundled("A");
        var duplicateRows = PlannerFixture.Inventory(row, row);
        var duplicateEdges = PlannerFixture.Inventory(PlannerFixture.Bundled("A", ["B", "B"]));
        Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() => ForSingle("A", duplicateRows)).Code);
        Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() => ForSingle("A", duplicateEdges)).Code);
    }

    [Fact]
    public void Unloaded_feature_cannot_claim_authoritative_runtime_edges()
    {
        var stale = PlannerFixture.Packaged("A", manifestDependencies: [new InventoryDependency("B", true)]) with
        { RuntimeDependencies = ["B"] };
        Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() => ForSingle("A", stale)).Code);
    }

    private static SelectionPlan ForSingle(string id, InventoryFeature row) => ForSingle(id, PlannerFixture.Inventory(row));

    private static SelectionPlan ForSingle(string id, HostInventory inventory)
    {
        var catalog = PlannerFixture.Catalog();
        return SelectionPlanner.Plan(catalog, PlannerFixture.Authored(catalog, add: [id], accepted: [id]), inventory);
    }
}
