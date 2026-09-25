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
    public void Init_refuses_unknown_profile_and_never_overwrites_an_existing_file()
    {
        using var temp = new TempDirectory("elsa-composition-init-");
        var outputPath = temp.File("composition.json");

        var unknown = DotnetElsa.Run("composition", "init", "--profile", "worker@1", "--output", outputPath);
        Assert.Equal(ToolExitCode.Refusal, unknown.ExitCode);
        Assert.Contains("composition-profile-unknown", unknown.Error, StringComparison.Ordinal);
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
