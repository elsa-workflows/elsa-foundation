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
        Assert.Empty(catalog.Groups);
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
