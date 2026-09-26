using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionInitCliTests
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
    public void Init_writes_a_pinned_composition_that_default_plan_inspects_exactly()
    {
        using var temp = new TempDirectory("elsa-composition-init-");
        var outputPath = temp.File("composition.json");
        var init = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1", "--output", outputPath);

        Assert.Equal(ToolExitCode.Success, init.ExitCode);
        Assert.Contains("embedded-runtime@1", init.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain("Password=", init.Text, StringComparison.OrdinalIgnoreCase);

        using var compositionDocument = JsonDocument.Parse(File.ReadAllText(outputPath));
        var composition = compositionDocument.RootElement;
        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal(catalog.Id, composition.GetProperty("catalog").GetProperty("id").GetString());
        Assert.Equal(catalog.Version, composition.GetProperty("catalog").GetProperty("version").GetString());
        Assert.Equal(catalog.Digest, composition.GetProperty("catalog").GetProperty("digest").GetString());
        Assert.Equal("foundation", composition.GetProperty("profile").GetProperty("origin").GetString());
        Assert.Equal(profile.Id, composition.GetProperty("profile").GetProperty("id").GetString());
        Assert.Equal(profile.Version, composition.GetProperty("profile").GetProperty("version").GetString());
        Assert.Equal(profile.Digest, composition.GetProperty("profile").GetProperty("digest").GetString());
        Assert.Equal(s_expectedMembers, Strings(composition.GetProperty("accepted").GetProperty("featureIds")));
        Assert.Empty(composition.GetProperty("accepted").GetProperty("locks").EnumerateArray());
        Assert.Empty(composition.GetProperty("groups").EnumerateArray());
        Assert.Empty(composition.GetProperty("add").EnumerateArray());
        Assert.Empty(composition.GetProperty("remove").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, composition.GetProperty("settings").ValueKind);
        Assert.Equal(JsonValueKind.Null, composition.GetProperty("resources").ValueKind);

        var plan = DotnetElsa.Run("composition", "plan", "--composition", outputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        Assert.DoesNotContain("Password=", plan.Text, StringComparison.OrdinalIgnoreCase);
        using var planDocument = JsonDocument.Parse(plan.Output);
        var root = planDocument.RootElement;
        Assert.Equal(s_expectedMembers, Strings(root.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(s_expectedMembers, Strings(root.GetProperty("accepted").GetProperty("featureIds")));
        Assert.Equal(16, root.GetProperty("reasons").GetArrayLength());
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "inventory-unverified");
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "persistence-unverified");
        Assert.DoesNotContain("runtimeReady", plan.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Init_with_diagnostics_group_keeps_exact_selection_provenance_and_required_edges()
    {
        using var temp = new TempDirectory("elsa-composition-init-");
        var outputPath = temp.File("composition.json");
        var init = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1",
            "--group", "diagnostics-ef@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.Success, init.ExitCode);

        using var compositionDocument = JsonDocument.Parse(File.ReadAllText(outputPath));
        var composition = compositionDocument.RootElement;
        var catalog = FoundationSelectionCatalog.Load();
        var group = Assert.Single(catalog.Groups);
        Assert.Equal("2", composition.GetProperty("catalog").GetProperty("version").GetString());
        Assert.Equal(group.Digest, Assert.Single(composition.GetProperty("groups").EnumerateArray())
            .GetProperty("digest").GetString());
        var expected = s_expectedMembers.Concat(group.Members).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, Strings(composition.GetProperty("accepted").GetProperty("featureIds")));

        var plan = DotnetElsa.Run("composition", "plan", "--composition", outputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        using var planDocument = JsonDocument.Parse(plan.Output);
        var root = planDocument.RootElement;
        Assert.Equal(expected, Strings(root.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(4, root.GetProperty("reasons").EnumerateArray().Count(reason =>
            reason.GetProperty("sourceKind").GetString() == "group" &&
            reason.GetProperty("sourceId").GetString() == "diagnostics-ef"));
        Assert.Equal(2, root.GetProperty("dependencyEvidence").EnumerateArray().Count(edge =>
            edge.GetProperty("evidenceKind").GetString() == "reviewed-definition" &&
            edge.GetProperty("evidenceSource").GetString() == "diagnostics-ef"));
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "persistence-unverified");
    }

    [Fact]
    public async Task Original_catalog_pin_still_plans_and_generates_without_a_catalog_file()
    {
        using var fixture = new CompositionBridgeFixture();
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1",
            "--output", fixture.OutputPath).ExitCode);
        var authored = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        const string originalDigest = "0a62934830eccd0e27e829ac7e17e6c2e44e85babd959a2aace53b15569f0149";
        authored["catalog"]!["version"] = "1";
        authored["catalog"]!["digest"] = originalDigest;
        authored["accepted"]!["catalogDigest"] = originalDigest;
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        var plan = DotnetElsa.Run("composition", "plan", "--composition", fixture.OutputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        using var planDocument = JsonDocument.Parse(plan.Output);
        Assert.Equal("1", planDocument.RootElement.GetProperty("catalog").GetProperty("version").GetString());
        Assert.Equal(s_expectedMembers, Strings(planDocument.RootElement.GetProperty("candidate").GetProperty("featureIds")));
        Assert.DoesNotContain(planDocument.RootElement.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "catalog-pin-unresolved");

        File.WriteAllText(Path.Join(fixture.HostDirectory, "shells.Production.json"),
            """{"CShells":{"Shells":{"default":{"Features":{}}}}}""");
        var generated = await PseudoTerminalCli.RunElsaAsync(
            "Type generate to write the candidate: ", "generate",
            ["composition", "generate", "--host-dir", fixture.HostDirectory, "--shell", "default",
                "--environment", "Production", "--composition", fixture.OutputPath, "--output-dir", fixture.CandidateDirectory]);
        Assert.Equal(ToolExitCode.Success, generated.ExitCode);
        var readback = CshellsSourceReader.Read(
            File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.json")),
            File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.Production.json")), "default");
        Assert.Equal(s_expectedMembers, readback.EnabledFeatureIds.ToArray());

        authored["catalog"]!["version"] = "unpublished";
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());
        var unknownPlan = DotnetElsa.Run("composition", "plan", "--composition", fixture.OutputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, unknownPlan.ExitCode);
        using var unknownPlanDocument = JsonDocument.Parse(unknownPlan.Output);
        Assert.Empty(unknownPlanDocument.RootElement.GetProperty("candidate").GetProperty("featureIds").EnumerateArray());
        Assert.Contains(unknownPlanDocument.RootElement.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "catalog-pin-unresolved");
        var unknownCandidate = Path.Join(fixture.HostDirectory, "unknown-candidate");
        var refused = DotnetElsa.Run("composition", "generate", "--host-dir", fixture.HostDirectory,
            "--shell", "default", "--environment", "Production", "--composition", fixture.OutputPath,
            "--output-dir", unknownCandidate);
        Assert.Equal(ToolExitCode.Refusal, refused.ExitCode);
        Assert.Contains("bridge-catalog-mismatch", refused.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(unknownCandidate));
    }

    [Fact]
    public void Init_refuses_unknown_profile_and_never_overwrites_an_existing_file()
    {
        using var temp = new TempDirectory("elsa-composition-init-");
        var outputPath = temp.File("composition.json");

        var unknown = DotnetElsa.Run("composition", "init", "--profile", "worker@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.Refusal, unknown.ExitCode);
        Assert.Contains("composition-profile-unknown", unknown.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));

        var unknownGroup = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1",
            "--group", "unreviewed@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.Refusal, unknownGroup.ExitCode);
        Assert.Contains("composition-group-unknown", unknownGroup.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));

        var duplicateGroup = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1",
            "--group", "diagnostics-ef@1", "--group", "diagnostics-ef@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.Refusal, duplicateGroup.ExitCode);
        Assert.Contains("composition-group-duplicate", duplicateGroup.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));

        File.WriteAllText(outputPath, "keep");
        var existing = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.ResolutionFailure, existing.ExitCode);
        Assert.Contains("composition-output-exists", existing.Error, StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Same_version_catalog_change_does_not_upgrade_an_authored_composition()
    {
        using var temp = new TempDirectory("elsa-composition-init-");
        var compositionPath = temp.File("composition.json");
        var catalogPath = temp.File("changed-catalog.json");
        Assert.Equal(ToolExitCode.Success,
            DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1", "--output", compositionPath).ExitCode);

        var catalog = FoundationSelectionCatalog.Load();
        var profile = Assert.Single(catalog.Profiles);
        var changedDraft = profile with { Description = profile.Description + " changed" };
        var changedProfile = changedDraft with { Digest = SelectionDigest.ComputeDefinitionDigest(changedDraft) };
        var changedCatalogDraft = catalog with { Profiles = [changedProfile] };
        var changedCatalog = changedCatalogDraft with { Digest = SelectionDigest.ComputeCatalogDigest(changedCatalogDraft) };
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(changedCatalog,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var plan = DotnetElsa.Run("composition", "plan", "--catalog", catalogPath,
            "--composition", compositionPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        using var planDocument = JsonDocument.Parse(plan.Output);
        Assert.Empty(planDocument.RootElement.GetProperty("candidate").GetProperty("featureIds").EnumerateArray());
        Assert.Contains(planDocument.RootElement.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "catalog-pin-unresolved");
        Assert.Contains(planDocument.RootElement.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "definition-pin-unresolved");

        using var fixture = new CompositionBridgeFixture();
        var generated = DotnetElsa.Run("composition", "generate",
            "--host-dir", fixture.HostDirectory,
            "--shell", "default",
            "--environment", "Production",
            "--catalog", catalogPath,
            "--composition", compositionPath,
            "--output-dir", fixture.CandidateDirectory);
        Assert.Equal(ToolExitCode.Refusal, generated.ExitCode);
        Assert.Contains("bridge-catalog-mismatch", generated.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
    }

    [Fact]
    public void Generate_uses_the_bundled_catalog_when_catalog_path_is_omitted()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();

        var generated = DotnetElsa.Run("composition", "generate",
            "--host-dir", fixture.HostDirectory,
            "--shell", "default",
            "--environment", "Production",
            "--composition", fixture.OutputPath,
            "--output-dir", fixture.CandidateDirectory);

        Assert.Equal(ToolExitCode.Refusal, generated.ExitCode);
        Assert.Contains("bridge-catalog-mismatch", generated.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
    }

    [Fact]
    public async Task Initialized_profile_generates_exact_candidate_and_removed_dependency_refuses()
    {
        using var fixture = new CompositionBridgeFixture();
        var init = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1", "--output", fixture.OutputPath);
        Assert.Equal(ToolExitCode.Success, init.ExitCode);
        File.WriteAllText(Path.Join(fixture.HostDirectory, "shells.Production.json"),
            """{"CShells":{"Shells":{"default":{"Features":{}}}},"ProductionAnnotation":{"Keep":"preserved"}}""");

        var generated = await PseudoTerminalCli.RunElsaAsync(
            "Type generate to write the candidate: ", "generate",
            ["composition", "generate", "--host-dir", fixture.HostDirectory, "--shell", "default",
                "--environment", "Production", "--composition", fixture.OutputPath, "--output-dir", fixture.CandidateDirectory]);
        Assert.True(generated.ExitCode == ToolExitCode.Success, generated.Output + generated.Error);
        Assert.True(generated.ResponseSent);
        var baseJson = File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.json"));
        var overlayJson = File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.Production.json"));
        var readback = CshellsSourceReader.Read(baseJson, overlayJson, "default");
        Assert.Equal(s_expectedMembers, readback.EnabledFeatureIds.ToArray());
        Assert.Equal(File.ReadAllText(Path.Join(fixture.HostDirectory, "shells.json")), baseJson);
        Assert.Equal(File.ReadAllText(Path.Join(fixture.HostDirectory, "shells.Staging.json")),
            File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.Staging.json")));
        Assert.Contains("feature-enabled", generated.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET", generated.Output, StringComparison.Ordinal);

        var mutable = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        mutable["remove"] = new System.Text.Json.Nodes.JsonArray(
            System.Text.Json.Nodes.JsonValue.Create("WorkflowsRuntimeResumption"));
        var accepted = mutable["accepted"]!["featureIds"]!.AsArray();
        var target = accepted.First(node => node?.GetValue<string>() == "WorkflowsRuntimeResumption");
        accepted.Remove(target);
        File.WriteAllText(fixture.OutputPath, mutable.ToJsonString());
        var blockedOutput = Path.Join(fixture.HostDirectory, "blocked-candidate");
        var blocked = DotnetElsa.Run("composition", "generate", "--host-dir", fixture.HostDirectory,
            "--shell", "default", "--environment", "Production", "--composition", fixture.OutputPath,
            "--output-dir", blockedOutput);
        Assert.Equal(ToolExitCode.Refusal, blocked.ExitCode);
        Assert.Contains("bridge-required-dependency-missing", blocked.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(blockedOutput));
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
}
