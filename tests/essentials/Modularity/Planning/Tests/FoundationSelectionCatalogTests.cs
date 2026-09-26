using System.Text.Json;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Tests;

public sealed class FoundationSelectionCatalogTests
{
    private static readonly string[] s_expectedMembers =
    [
        "ActivitiesControlFlow",
        "ActivitiesPrimitives",
        "ActivitiesRuntime",
        "ActivitiesSequence",
        "ApiCapabilities",
        "Events",
        "Expressions",
        "FileSystemDistributedLocking",
        "Mediator",
        "Primitives",
        "Serialization",
        "Tasks",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];
    private static readonly string[] s_diagnosticsMembers =
    [
        "DiagnosticsOpenTelemetry",
        "DiagnosticsOpenTelemetryEntityFrameworkCore",
        "DiagnosticsStructuredLogs",
        "DiagnosticsStructuredLogsEntityFrameworkCore"
    ];
    private const string OriginalCatalogDigest = "0a62934830eccd0e27e829ac7e17e6c2e44e85babd959a2aace53b15569f0149";

    [Fact]
    public void Bundled_profile_plans_to_the_exact_reviewed_set_with_profile_provenance()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles);
        var authored = Authored(catalog, profile, profile.Members);
        var plan = SelectionPlanner.Plan(catalog, authored);

        Assert.Equal("elsa-foundation", catalog.Id);
        Assert.Equal("embedded-runtime", profile.Id);
        Assert.Equal("1", profile.Version);
        Assert.Equal(s_expectedMembers, plan.SelectedFeatureIds.ToArray());
        Assert.Equal(16, plan.Reasons.Length);
        Assert.All(plan.Reasons, reason =>
        {
            Assert.Equal("profile", reason.SourceKind);
            Assert.Equal("embedded-runtime", reason.SourceId);
            Assert.Equal("1", reason.SourceVersion);
        });
        Assert.Contains("WorkflowsRuntimeApi", plan.SelectedFeatureIds);
        Assert.Contains("FileSystemDistributedLocking", plan.SelectedFeatureIds);
        Assert.Contains(plan.Findings, finding => finding.Code == "inventory-unverified");
        Assert.Contains(plan.Findings, finding => finding.Code == "persistence-unverified");
        Assert.Equal(4, plan.DependencyEvidence.Length);
        Assert.Equal(catalog.Digest, SelectionDigest.ComputeCatalogDigest(catalog));
        Assert.Equal(profile.Digest, SelectionDigest.ComputeDefinitionDigest(profile));
    }

    [Fact]
    public void Bundled_catalog_contains_only_reviewed_selection_content()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var json = JsonSerializer.Serialize(catalog);

        Assert.DoesNotContain("Server=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocksFolderPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("signingKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("migrationAuthorization", json, StringComparison.OrdinalIgnoreCase);
        var group = Assert.Single(catalog.Groups);
        Assert.Equal("2", catalog.Version);
        Assert.Equal("diagnostics-ef", group.Id);
        Assert.Equal("1", group.Version);
        Assert.Equal(s_diagnosticsMembers, group.Members.ToArray());
        Assert.Equal(2, group.DependencyExplanations.Length);
        Assert.All(group.DependencyExplanations, edge => Assert.Equal("required", edge.Mode));
        Assert.Contains(group.DependencyExplanations, edge =>
            edge.FeatureId == "DiagnosticsOpenTelemetryEntityFrameworkCore" &&
            edge.DependencyId == "DiagnosticsOpenTelemetry");
        Assert.Contains(group.DependencyExplanations, edge =>
            edge.FeatureId == "DiagnosticsStructuredLogsEntityFrameworkCore" &&
            edge.DependencyId == "DiagnosticsStructuredLogs");
        Assert.Equal(group.Digest, SelectionDigest.ComputeDefinitionDigest(group));
        Assert.Equal(catalog.Digest, SelectionDigest.ComputeCatalogDigest(catalog));
    }

    [Fact]
    public void Original_bundled_snapshot_resolves_only_its_exact_pin()
    {
        var originalPin = new CatalogPin("elsa-foundation", "1", OriginalCatalogDigest);
        var original = FoundationSelectionCatalog.LoadFor(originalPin);

        Assert.Equal("1", original.Version);
        Assert.Equal(OriginalCatalogDigest, original.Digest);
        Assert.Equal(OriginalCatalogDigest, SelectionDigest.ComputeCatalogDigest(original));
        Assert.Empty(original.Groups);
        Assert.Equal(s_expectedMembers, Assert.Single(original.Profiles).Members.ToArray());

        Assert.Equal("2", FoundationSelectionCatalog.LoadFor(originalPin with { Digest = new string('0', 64) }).Version);
        Assert.Equal("2", FoundationSelectionCatalog.LoadFor(originalPin with { Version = "unknown" }).Version);
    }

    [Fact]
    public void Published_group_keeps_an_explicitly_removed_required_base_feature_unresolved()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var group = Assert.Single(catalog.Groups);
        var authored = new AuthoredComposition(
            "1",
            new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            null,
            [new DefinitionReference("foundation", "group", group.Id, group.Version, group.Digest)],
            [],
            ["DiagnosticsOpenTelemetry"],
            new AcceptedSelection(catalog.Digest, [.. group.Members.Where(id => id != "DiagnosticsOpenTelemetry")], []),
            null,
            null);

        var plan = SelectionPlanner.Plan(catalog, authored);

        Assert.DoesNotContain("DiagnosticsOpenTelemetry", plan.SelectedFeatureIds);
        Assert.Contains("DiagnosticsOpenTelemetryEntityFrameworkCore", plan.SelectedFeatureIds);
        Assert.Contains(plan.DependencyEvidence, edge =>
            edge.FeatureId == "DiagnosticsOpenTelemetryEntityFrameworkCore" &&
            edge.DependencyId == "DiagnosticsOpenTelemetry" &&
            edge.Mode == "required" && !edge.TargetSelected);
        Assert.Contains(plan.Findings, finding =>
            finding.Code == "required-dependency-missing" &&
            finding.FeatureId == "DiagnosticsOpenTelemetryEntityFrameworkCore" &&
            finding.DependencyId == "DiagnosticsOpenTelemetry");
    }

    [Fact]
    public void Bundled_catalog_reader_rejects_modified_same_version_content()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles);
        var changedProfile = profile with { Description = profile.Description + " changed" };
        var changedCatalog = catalog with { Profiles = [changedProfile] };
        var json = JsonSerializer.Serialize(changedCatalog, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal("digest-mismatch", Assert.Throws<SelectionDocumentException>(() => SelectionJsonReader.ParseCatalog(json)).Code);
    }

    private static AuthoredComposition Authored(
        SelectionCatalog catalog,
        SelectionDefinition profile,
        System.Collections.Immutable.ImmutableArray<string> acceptedIds) =>
        new(
            "1",
            new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            new DefinitionReference("foundation", profile.Kind, profile.Id, profile.Version, profile.Digest),
            [],
            [],
            [],
            new AcceptedSelection(catalog.Digest, acceptedIds, []),
            null,
            null);
}
