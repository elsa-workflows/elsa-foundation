using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Tests;

public sealed class ReresolutionTests
{
    [Fact]
    public void Candidate_reports_membership_reason_dependency_lock_and_resource_drift_without_changing_old_input()
    {
        var oldProfile = PlannerFixture.Definition("profile", "starter", ["A"], rationale: "first", explanations: [new DependencyExplanation("A", "B", "optional", "first reason")]);
        var newProfile = PlannerFixture.Definition("profile", "starter", ["A", "C"], version: "2", rationale: "second", explanations: [new DependencyExplanation("A", "B", "optional", "second reason")]);
        var oldCatalog = PlannerFixture.Catalog(profiles: [oldProfile], version: "1");
        var newCatalog = PlannerFixture.Catalog(profiles: [newProfile], version: "2");
        var oldAuthored = PlannerFixture.Authored(oldCatalog, PlannerFixture.Ref(oldProfile), accepted: ["A"]);
        var newAuthored = PlannerFixture.Authored(newCatalog, PlannerFixture.Ref(newProfile), accepted: ["A"]);
        var oldInventory = PlannerFixture.Inventory(PlannerFixture.Packaged("A", manifestDependencies: [new InventoryDependency("B", false)]));
        var newInventory = PlannerFixture.Inventory(
            PlannerFixture.Packaged("A", manifestDependencies: [new InventoryDependency("D", false)]) with
            { Package = new InventoryPackage("Elsa.Test.A", "2.0.0", new string('b', 64)) },
            PlannerFixture.Bundled("C"));
        var old = new SelectionInputs(oldCatalog, oldAuthored, oldInventory, Persistence: new PersistenceEvidence("checked", "host-default", ["primary"], []));
        var candidate = new SelectionInputs(newCatalog, newAuthored, newInventory, Persistence: new PersistenceEvidence("checked", "host-default", ["reporting"], []));

        var before = PlannerFixture.Json(oldAuthored);
        var diff = SelectionReresolver.Compare(old, candidate);

        Assert.Equal(["C"], diff.AddedFeatureIds.ToArray());
        Assert.Empty(diff.RemovedFeatureIds);
        Assert.NotEmpty(diff.ChangedReasons);
        Assert.NotEmpty(diff.ChangedReviewedExplanations);
        Assert.Contains(diff.ChangedDependencyFindings, x => x.Old?.DependencyId == "B");
        Assert.Contains(diff.ChangedDependencyFindings, x => x.Candidate?.DependencyId == "D");
        Assert.Contains(diff.ChangedLocks, x => x.Old?.PackageVersion == "1.0.0");
        Assert.Contains(diff.ChangedLocks, x => x.Candidate?.PackageVersion == "2.0.0");
        Assert.Equal("resource-reference-changed", diff.SettingsResourceImpact);
        Assert.Equal(before, PlannerFixture.Json(oldAuthored));
        Assert.Equal(["A"], oldAuthored.Accepted.FeatureIds.ToArray());
        Assert.Contains(diff.CandidatePlan.Findings, x => x.Code == "candidate-re-resolution");
    }

    [Fact]
    public void Reused_catalog_or_definition_version_with_changed_content_refuses()
    {
        var first = PlannerFixture.Definition("profile", "starter", ["A"]);
        var changed = PlannerFixture.Definition("profile", "starter", ["B"]);
        var oldCatalog = PlannerFixture.Catalog(profiles: [first]);
        var changedCatalog = PlannerFixture.Catalog(profiles: [changed]);
        var old = new SelectionInputs(oldCatalog, PlannerFixture.Authored(oldCatalog, PlannerFixture.Ref(first), accepted: ["A"]));
        var candidate = new SelectionInputs(changedCatalog, PlannerFixture.Authored(changedCatalog, PlannerFixture.Ref(changed), accepted: ["B"]));
        Assert.Equal("immutable-version-conflict", Assert.Throws<SelectionDocumentException>(() => SelectionReresolver.Compare(old, candidate)).Code);

        var nextCatalog = PlannerFixture.Catalog(profiles: [changed], version: "2");
        var sameDefinitionVersion = new SelectionInputs(nextCatalog, PlannerFixture.Authored(nextCatalog, PlannerFixture.Ref(changed), accepted: ["B"]));
        Assert.Equal("immutable-version-conflict", Assert.Throws<SelectionDocumentException>(() => SelectionReresolver.Compare(old, sameDefinitionVersion)).Code);
    }

    [Fact]
    public void Reused_workspace_profile_version_with_changed_content_refuses()
    {
        var catalog = PlannerFixture.Catalog();
        var first = PlannerFixture.Definition("profile", "mine", ["A"]);
        var changed = PlannerFixture.Definition("profile", "mine", ["B"]);
        var old = new SelectionInputs(catalog, PlannerFixture.Authored(catalog, PlannerFixture.Ref(first, "workspace"), accepted: ["A"]), WorkspaceProfiles: [new WorkspaceProfile("1", first)]);
        var candidate = new SelectionInputs(catalog, PlannerFixture.Authored(catalog, PlannerFixture.Ref(changed, "workspace"), accepted: ["B"]), WorkspaceProfiles: [new WorkspaceProfile("1", changed)]);
        Assert.Equal("immutable-version-conflict", Assert.Throws<SelectionDocumentException>(() => SelectionReresolver.Compare(old, candidate)).Code);
    }

    [Fact]
    public void Unchanged_resource_references_without_setting_evidence_remain_unverified()
    {
        var catalog = PlannerFixture.Catalog();
        var inputs = new SelectionInputs(catalog, PlannerFixture.Authored(catalog, add: ["A"], accepted: ["A"]));
        var diff = SelectionReresolver.Compare(inputs, inputs);
        Assert.Empty(diff.AddedFeatureIds);
        Assert.Empty(diff.ChangedReasons);
        Assert.Equal("unverified", diff.SettingsResourceImpact);
    }
}
