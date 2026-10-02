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
    private static readonly string[] s_workerMembers =
    [
        "ActivitiesControlFlow",
        "ActivitiesPrimitives",
        "ActivitiesRuntime",
        "ActivitiesSequence",
        "ApiCapabilities",
        "Events",
        "Expressions",
        "FileSystemDistributedLocking",
        "FoundationIdentityAbstractions",
        "FoundationIdentityOidc",
        "IdentityIamEntityFrameworkCore",
        "Mediator",
        "Primitives",
        "Serialization",
        "Tasks",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];
    private static readonly string[] s_workerDependencyEdges =
    [
        "WorkflowsRuntimeApi->ApiCapabilities",
        "WorkflowsRuntimeEntityFrameworkCore->WorkflowsRuntimeResumption",
        "WorkflowsRuntimeResumption->Tasks",
        "WorkflowsRuntimeTriggers->WorkflowsRuntimeApi"
    ];
    private const string OriginalCatalogDigest = "0a62934830eccd0e27e829ac7e17e6c2e44e85babd959a2aace53b15569f0149";
    private const string PreviousCatalogDigest = "42afc8ccd1f18c972d882f1e214c961d5aad0788a6f0974e8964bb6e012bda15";
    private const string WorkerProfileDigest = "e46f8092771171ad63b0ca81bf307f5ad7ad7a9ca4929dd311bfac6af3cf8fa1";
    private const string CurrentCatalogDigest = "6563d77f116b7aefb2a67425f28b72cab736297d4e46df964bf9fb507cf91c3c";

    [Fact]
    public void Bundled_profile_plans_to_the_exact_reviewed_set_with_profile_provenance()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles, x => x.Id == "embedded-runtime");
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
    public void Bundled_worker_profile_plans_exact_reviewed_members_with_provenance_and_dependencies()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles, x => x.Id == "worker-http");
        var authored = Authored(catalog, profile, profile.Members);
        var plan = SelectionPlanner.Plan(catalog, authored);

        Assert.Equal("3", catalog.Version);
        Assert.Equal(CurrentCatalogDigest, catalog.Digest);
        Assert.Equal("1", profile.Version);
        Assert.Equal(WorkerProfileDigest, profile.Digest);
        Assert.Equal(s_workerMembers, profile.Members.ToArray());
        Assert.Equal(s_workerMembers, plan.SelectedFeatureIds.ToArray());
        Assert.Equal(19, plan.Reasons.Length);
        Assert.All(plan.Reasons, reason =>
        {
            Assert.Equal("profile", reason.SourceKind);
            Assert.Equal("worker-http", reason.SourceId);
            Assert.Equal("1", reason.SourceVersion);
        });
        Assert.Equal(4, plan.DependencyEvidence.Length);
        Assert.All(plan.DependencyEvidence, edge =>
        {
            Assert.Equal("reviewed-definition", edge.EvidenceKind);
            Assert.Equal("worker-http", edge.EvidenceSource);
            Assert.Equal("required", edge.Mode);
            Assert.True(edge.TargetSelected);
        });
        Assert.Equal(s_workerDependencyEdges, plan.DependencyEvidence
            .Select(edge => $"{edge.FeatureId}->{edge.DependencyId}").ToArray());
        Assert.Contains(plan.Findings, finding => finding.Code == "inventory-unverified");
        Assert.Contains(plan.Findings, finding => finding.Code == "persistence-unverified");
        Assert.Equal(WorkerProfileDigest, SelectionDigest.ComputeDefinitionDigest(profile));
        Assert.Equal(CurrentCatalogDigest, SelectionDigest.ComputeCatalogDigest(catalog));
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
        var group = Assert.Single(catalog.Groups, x => x.Id == "diagnostics-ef");
        Assert.Equal("3", catalog.Version);
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
        var originalProfile = Assert.Single(original.Profiles, x => x.Id == "embedded-runtime");
        Assert.Equal("84765f93718964b00ee47f4dae07dbf2572f60d53741fd7ba68426bc696928ea", originalProfile.Digest);
        Assert.Equal(s_expectedMembers, originalProfile.Members.ToArray());

        Assert.Equal("3", FoundationSelectionCatalog.LoadFor(originalPin with { Digest = new string('0', 64) }).Version);
        Assert.Equal("3", FoundationSelectionCatalog.LoadFor(originalPin with { Version = "unknown" }).Version);
    }

    [Fact]
    public void Previous_bundled_snapshot_and_definitions_resolve_only_their_exact_pin()
    {
        var previousPin = new CatalogPin("elsa-foundation", "2", PreviousCatalogDigest);
        var previous = FoundationSelectionCatalog.LoadFor(previousPin);

        Assert.Equal("2", previous.Version);
        Assert.Equal(PreviousCatalogDigest, previous.Digest);
        Assert.Equal(PreviousCatalogDigest, SelectionDigest.ComputeCatalogDigest(previous));
        var profile = Assert.Single(previous.Profiles, x => x.Id == "embedded-runtime");
        Assert.Equal("84765f93718964b00ee47f4dae07dbf2572f60d53741fd7ba68426bc696928ea", profile.Digest);
        Assert.Equal(profile.Digest, SelectionDigest.ComputeDefinitionDigest(profile));
        Assert.Equal(s_expectedMembers, profile.Members.ToArray());
        var group = Assert.Single(previous.Groups, x => x.Id == "diagnostics-ef");
        Assert.Equal("77c5f3cfed1ba29da2ec3d54bd1479b46799d9178c4ee5b93f2a3afda6d2cdae", group.Digest);
        Assert.Equal(group.Digest, SelectionDigest.ComputeDefinitionDigest(group));
        Assert.Equal(s_diagnosticsMembers, group.Members.ToArray());

        var current = FoundationSelectionCatalog.Load();
        var mismatchedDigestPin = previousPin with { Digest = new string('0', 64) };
        var unknownVersionPin = previousPin with { Version = "unpublished" };
        Assert.Equal(current.Digest, FoundationSelectionCatalog.LoadFor(mismatchedDigestPin).Digest);
        Assert.Equal(current.Digest, FoundationSelectionCatalog.LoadFor(unknownVersionPin).Digest);

        var workerProfile = Assert.Single(current.Profiles, x => x.Id == "worker-http");
        var authored = Authored(current, workerProfile, workerProfile.Members)
            with { Catalog = mismatchedDigestPin, Accepted = new AcceptedSelection(mismatchedDigestPin.Digest, workerProfile.Members, []) };
        Assert.Contains(SelectionPlanner.Plan(current, authored).Findings, finding => finding.Code == "catalog-pin-unresolved");
        authored = authored with
        {
            Catalog = unknownVersionPin,
            Accepted = new AcceptedSelection(unknownVersionPin.Digest, workerProfile.Members, [])
        };
        Assert.Contains(SelectionPlanner.Plan(current, authored).Findings, finding => finding.Code == "catalog-pin-unresolved");
    }

    [Fact]
    public void Published_group_keeps_an_explicitly_removed_required_base_feature_unresolved()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var group = Assert.Single(catalog.Groups, x => x.Id == "diagnostics-ef");
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
        var profile = Assert.Single(catalog.Profiles, x => x.Id == "embedded-runtime");
        var changedProfile = profile with { Description = profile.Description + " changed" };
        var changedCatalog = catalog with
        {
            Profiles = [.. catalog.Profiles.Select(existing => existing.Id == profile.Id ? changedProfile : existing)]
        };
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
