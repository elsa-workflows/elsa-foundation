using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Tests;

public sealed class SelectionPlanContractTests
{
    [Fact]
    public void Opaque_settings_and_resource_values_never_enter_the_plan_result()
    {
        const string secret = "Server=private.example;Password=SENSITIVE_SENTINEL";
        var catalog = PlannerFixture.Catalog();
        using var settings = JsonDocument.Parse(PlannerFixture.Json(new { provider = "PostgreSql", connectionString = secret }));
        using var resources = JsonDocument.Parse(PlannerFixture.Json(new { primary = new { connectionString = secret } }));
        var authored = PlannerFixture.Authored(catalog, add: ["A"], accepted: ["A"],
            settings: settings.RootElement.Clone(), resources: resources.RootElement.Clone());
        var plan = SelectionPlanner.Plan(catalog, authored, PlannerFixture.Inventory(PlannerFixture.Bundled("A")));

        Assert.DoesNotContain(secret, PlannerFixture.Json(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("SENSITIVE_SENTINEL", PlannerFixture.Json(plan), StringComparison.Ordinal);
        Assert.Equal(["A"], plan.SelectedFeatureIds.ToArray());
    }

    [Fact]
    public void Persistence_is_unchecked_without_supplied_evidence_and_only_safe_references_are_exposed()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog);
        var uncheckedPlan = SelectionPlanner.Plan(catalog, authored);
        Assert.Equal("unchecked", uncheckedPlan.Persistence.Status);
        Assert.Contains(uncheckedPlan.Findings, x => x.Code == "persistence-unverified" && x.Severity == "unresolved");

        var checkedPlan = SelectionPlanner.Plan(catalog, authored, persistence: new PersistenceEvidence("checked", "host-default", ["primary-db"], []));
        Assert.Equal(["primary-db"], checkedPlan.Persistence.ResourceReferences.ToArray());
        Assert.DoesNotContain(checkedPlan.Findings, x => x.Code == "persistence-unverified");
        Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() =>
            SelectionPlanner.Plan(catalog, authored, persistence: new PersistenceEvidence("checked", "host-default", ["Server=private;Password=secret"], []))).Code);
    }

    [Fact]
    public void Typed_callers_cannot_skip_definition_or_authored_integrity_validation()
    {
        var definition = PlannerFixture.Definition("profile", "starter", ["A"]);
        var catalog = PlannerFixture.Catalog(profiles: [definition]);
        var authored = PlannerFixture.Authored(catalog, PlannerFixture.Ref(definition), accepted: ["A"]);
        var tampered = new WorkspaceProfile("1", definition with { Members = ["B"] });
        Assert.Equal("digest-mismatch", Assert.Throws<SelectionDocumentException>(() =>
            SelectionPlanner.Plan(PlannerFixture.Catalog(), PlannerFixture.Authored(PlannerFixture.Catalog()), workspaceProfiles: [tampered])).Code);

        var duplicate = authored with { Add = ["A", "A"] };
        Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() => SelectionPlanner.Plan(catalog, duplicate)).Code);
    }

    [Fact]
    public void Typed_callers_cannot_supply_malformed_pins_or_echo_connection_like_package_locks()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog, add: ["A"], accepted: ["A"]);
        var badCatalogPin = authored with { Catalog = authored.Catalog with { Digest = "not-a-digest" } };
        var badAcceptedPin = authored with { Accepted = authored.Accepted with { CatalogDigest = "not-a-digest" } };
        var badProfileRef = authored with { Profile = new DefinitionReference("foundation", "profile", "p", "1", "not-a-digest") };
        var leakedLock = authored with { Accepted = authored.Accepted with
        {
            Locks = [new FeatureLock("A", "package", "Server=private;Password=secret", "1.0.0", new string('a', 64), "accepted")]
        } };
        var malformedLockDigest = authored with { Accepted = authored.Accepted with
        {
            Locks = [new FeatureLock("A", "package", "Elsa.Test.A", "1.0.0", "not-a-digest", "accepted")]
        } };

        foreach (var malformed in new[] { badCatalogPin, badAcceptedPin, badProfileRef, leakedLock, malformedLockDigest })
            Assert.Equal("invalid-field", Assert.Throws<SelectionDocumentException>(() => SelectionPlanner.Plan(catalog, malformed)).Code);
    }

    [Fact]
    public void Public_result_serialization_is_stable_across_definition_and_choice_order()
    {
        var first = PlannerFixture.Definition("group", "first", ["B", "A"]);
        var second = PlannerFixture.Definition("group", "second", ["C", "B"]);
        var forward = PlannerFixture.Catalog(groups: [first, second]);
        var reversed = PlannerFixture.Catalog(groups: [second, first]);
        var inventory = PlannerFixture.Inventory(PlannerFixture.Bundled("A"), PlannerFixture.Bundled("B"), PlannerFixture.Bundled("C"));
        var left = SelectionPlanner.Plan(forward, PlannerFixture.Authored(forward,
            groups: [PlannerFixture.Ref(first), PlannerFixture.Ref(second)], accepted: ["A", "B", "C"]), inventory);
        var right = SelectionPlanner.Plan(reversed, PlannerFixture.Authored(reversed,
            groups: [PlannerFixture.Ref(second), PlannerFixture.Ref(first)], accepted: ["C", "B", "A"]), inventory);

        Assert.Equal(PlannerFixture.Json(left), PlannerFixture.Json(right));
        Assert.DoesNotContain("RuntimeReady", PlannerFixture.Json(left), StringComparison.Ordinal);
    }
}
