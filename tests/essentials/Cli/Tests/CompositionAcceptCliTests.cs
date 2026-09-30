using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionAcceptCliTests
{
    private const string Canary = "COMPOSITION_ACCEPT_OPAQUE_CANARY_NOT_A_SECRET";
    private const string DependencyCanary = "COMPOSITION_ACCEPT_DEPENDENCY_REASON_CANARY_NOT_A_SECRET";
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task Edited_selection_is_planned_accepted_and_generated_with_exact_readback()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        EditAddC(fixture.OutputPath);

        var plan = DotnetElsa.Run("composition", "plan", "--catalog", fixture.CatalogPath,
            "--composition", fixture.OutputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        using (var planDocument = JsonDocument.Parse(plan.Output))
        {
            var planRoot = planDocument.RootElement;
            Assert.Equal(new[] { "A", "C" }, Strings(planRoot.GetProperty("candidate").GetProperty("featureIds")));
            Assert.Equal(new[] { "A" }, Strings(planRoot.GetProperty("accepted").GetProperty("featureIds")));
        }

        var staleCandidate = Path.Join(Path.GetDirectoryName(fixture.OutputPath)!, "stale-candidate");
        var stale = fixture.RunGenerate(compositionPath: fixture.OutputPath, outputDirectory: staleCandidate);
        Assert.Equal(ToolExitCode.Refusal, stale.ExitCode);
        Assert.Contains("bridge-selection-drift", stale.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(staleCandidate));

        var accepted = await fixture.RunAcceptInteractiveAsync("accept");
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        Assert.True(accepted.ResponseSent);
        Assert.False(accepted.TimedOut);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.AcceptedOutputPath));
        Assert.DoesNotContain(Canary, accepted.Output + accepted.Error, StringComparison.Ordinal);
        using (var preview = ReadPreview(accepted.Output + accepted.Error))
        {
            Assert.Equal(new[] { "A", "C" }, Strings(preview.RootElement.GetProperty("candidateFeatureIds")));
            Assert.Equal(new[] { "A" }, Strings(preview.RootElement.GetProperty("acceptedFeatureIds")));
            Assert.Equal(new[] { "C" }, Strings(preview.RootElement.GetProperty("addedFeatureIds")));
        }

        var acceptedPlan = DotnetElsa.Run("composition", "plan", "--catalog", fixture.CatalogPath,
            "--composition", fixture.AcceptedOutputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, acceptedPlan.ExitCode);
        using (var planDocument = JsonDocument.Parse(acceptedPlan.Output))
        {
            var root = planDocument.RootElement;
            Assert.Equal(new[] { "A", "C" }, Strings(root.GetProperty("candidate").GetProperty("featureIds")));
            Assert.Equal(new[] { "A", "C" }, Strings(root.GetProperty("accepted").GetProperty("featureIds")));
            Assert.DoesNotContain(root.GetProperty("findings").EnumerateArray(), finding =>
                finding.GetProperty("code").GetString() == "candidate-re-resolution");
        }

        var candidate = Path.Join(Path.GetDirectoryName(fixture.OutputPath)!, "accepted-candidate");
        var generated = await fixture.RunGenerateInteractiveAsync(
            "generate",
            compositionPath: fixture.AcceptedOutputPath,
            outputDirectory: candidate);
        Assert.Equal(ToolExitCode.Success, generated.ExitCode);
        Assert.True(Directory.Exists(candidate));
        var readback = CshellsSourceReader.Read(
            File.ReadAllText(Path.Join(candidate, "shells.json")),
            File.ReadAllText(Path.Join(candidate, "shells.Production.json")), "default");
        Assert.Equal(new[] { "A", "C" }, readback.EnabledFeatureIds);
        var mutated = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!;
        Assert.Empty(mutated["accepted"]!["locks"]!.AsArray());
        mutated["accepted"]!["featureIds"] = new JsonArray("A");
        File.WriteAllText(fixture.AcceptedOutputPath, mutated.ToJsonString(s_json));
        var revertedAcceptance = fixture.RunGenerate(
            compositionPath: fixture.AcceptedOutputPath,
            outputDirectory: staleCandidate);
        Assert.Contains("bridge-selection-drift", revertedAcceptance.Error, StringComparison.Ordinal);
        Assert.Equal(ToolExitCode.Refusal, revertedAcceptance.ExitCode);
        Assert.False(Directory.Exists(staleCandidate));
        var findings = ReadFindingCodes(acceptedPlan.Output);
        Assert.Contains("inventory-unverified", findings);
        Assert.Contains("persistence-unverified", findings);
    }

    [Fact]
    public async Task Accept_filters_locks_and_preserves_opaque_values_without_rendering_them()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        var accepted = authored["accepted"]!.AsObject();
        accepted["featureIds"] = new JsonArray("A", "B", "C");
        accepted["locks"] = new JsonArray(Lock("A", "package-a-" + Canary), Lock("B", "package-b"),
            new JsonObject { ["featureId"] = "C", ["kind"] = "hostBundled", ["evidenceSource"] = "historical" });
        authored["add"] = new JsonArray("A", "C", "D");
        var expectedLocks = new JsonArray(accepted["locks"]![0]!.DeepClone(), accepted["locks"]![2]!.DeepClone());
        authored["settings"]!["opaque"] = new JsonObject { ["nested"] = Canary };
        authored["resources"]!["opaque"] = Canary;
        var settings = authored["settings"]!.DeepClone();
        var resources = authored["resources"]!.DeepClone();
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));

        var run = await fixture.RunAcceptInteractiveAsync("accept");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.DoesNotContain(Canary, run.Output + run.Error, StringComparison.Ordinal);
        using var preview = ReadPreview(run.Output + run.Error);
        Assert.Equal(new[] { "A", "C" }, Strings(preview.RootElement.GetProperty("retainedLockFeatureIds")));
        Assert.Equal(new[] { "B" }, Strings(preview.RootElement.GetProperty("droppedLockFeatureIds")));
        Assert.Equal(new[] { "B" }, Strings(preview.RootElement.GetProperty("removedFeatureIds")));

        var output = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        Assert.True(JsonNode.DeepEquals(settings, output["settings"]));
        Assert.True(JsonNode.DeepEquals(resources, output["resources"]));
        Assert.Equal(new[] { "A", "C", "D" }, Strings(output["accepted"]!["featureIds"]!.AsArray()));
        Assert.True(JsonNode.DeepEquals(expectedLocks, output["accepted"]!["locks"]));
        AssertOnlyAcceptedSelectionChanged(authored, output);
    }

    [Fact]
    public async Task Bundled_profile_and_group_edits_keep_their_pins_and_accept_without_new_locks()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        var initialized = DotnetElsa.Run("composition", "init", "--profile", "embedded-runtime@1",
            "--group", "diagnostics-ef@1", "--output", fixture.OutputPath);
        Assert.Equal(ToolExitCode.Success, initialized.ExitCode);
        var input = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        input["add"] = new JsonArray("CustomCapability");
        File.WriteAllText(fixture.OutputPath, input.ToJsonString(s_json));

        var run = await PseudoTerminalCli.RunElsaAsync("Type accept to write the accepted composition: ", "accept",
            ["composition", "accept", "--composition", fixture.OutputPath, "--output", fixture.AcceptedOutputPath]);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        var output = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        AssertOnlyAcceptedSelectionChanged(input, output);
        Assert.Contains("CustomCapability", Strings(output["accepted"]!["featureIds"]!.AsArray()));
        Assert.Equal(21, output["accepted"]!["featureIds"]!.AsArray().Count);
        Assert.Empty(output["accepted"]!["locks"]!.AsArray());
        using var preview = ReadPreview(run.Output + run.Error);
        Assert.Contains(preview.RootElement.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("code").GetString() == "inventory-unverified");
    }

    [Fact]
    public async Task Workspace_profile_drives_plan_accept_and_generation_with_exact_readback()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var unused = fixture.WriteWorkspaceProfile(
            "unused-profile.json", "unused-profile", "1", ["A"], rationale: Canary);
        var selected = fixture.WriteWorkspaceProfile(
            "selected-profile.json", "local-profile", "1", ["A", "C"],
            rationale: Canary,
            explanations: [new DependencyExplanation("A", "C", "optional", DependencyCanary)]);
        var profiles = new[] { unused.Path, selected.Path };
        var input = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        input["profile"] = JsonSerializer.SerializeToNode(
            new DefinitionReference("workspace", "profile", selected.Definition.Id, selected.Definition.Version, selected.Definition.Digest), s_json);
        input["add"] = new JsonArray();
        input["remove"] = new JsonArray();
        File.WriteAllText(fixture.OutputPath, input.ToJsonString(s_json));

        var initialPlan = DotnetElsa.Run(
            "composition", "plan", "--catalog", fixture.CatalogPath,
            "--composition", fixture.OutputPath,
            "--workspace-profile", unused.Path,
            "--workspace-profile", selected.Path,
            "--format", "json");
        Assert.Equal(ToolExitCode.Success, initialPlan.ExitCode);
        using (var initialPlanDocument = JsonDocument.Parse(initialPlan.Output))
        {
            Assert.Equal(new[] { "A", "C" }, Strings(initialPlanDocument.RootElement.GetProperty("candidate").GetProperty("featureIds")));
            Assert.Equal(new[] { "A" }, Strings(initialPlanDocument.RootElement.GetProperty("accepted").GetProperty("featureIds")));
        }

        var run = await fixture.RunAcceptInteractiveAsync("accept", profiles);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        AssertCanariesAbsent(initialPlan.Text + run.Output + run.Error);
        using var preview = ReadPreview(run.Output + run.Error);
        Assert.Equal(new[] { "A", "C" }, Strings(preview.RootElement.GetProperty("candidateFeatureIds")));
        Assert.Equal(new[] { "A" }, Strings(preview.RootElement.GetProperty("acceptedFeatureIds")));
        var output = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        AssertOnlyAcceptedSelectionChanged(input, output);
        Assert.Equal(new[] { "A", "C" }, Strings(output["accepted"]!["featureIds"]!.AsArray()));

        var planned = DotnetElsa.Run(
            "composition", "plan", "--catalog", fixture.CatalogPath,
            "--composition", fixture.AcceptedOutputPath,
            "--workspace-profile", unused.Path,
            "--workspace-profile", selected.Path,
            "--format", "json");
        Assert.Equal(ToolExitCode.Success, planned.ExitCode);
        using var plan = JsonDocument.Parse(planned.Output);
        Assert.Equal(new[] { "A", "C" }, Strings(plan.RootElement.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(new[] { "A", "C" }, Strings(plan.RootElement.GetProperty("accepted").GetProperty("featureIds")));
        Assert.DoesNotContain("candidate-re-resolution", ReadFindingCodes(planned.Output));
        Assert.Contains("inventory-unverified", ReadFindingCodes(planned.Output));
        Assert.Contains("persistence-unverified", ReadFindingCodes(planned.Output));

        var source = Directory.GetFiles(fixture.HostDirectory, "*.json")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);
        var generated = await fixture.RunGenerateInteractiveAsync(
            "generate",
            handoffHost: "workbench-a",
            compositionPath: fixture.AcceptedOutputPath,
            workspaceProfilePaths: profiles);

        Assert.Equal(ToolExitCode.Success, generated.ExitCode);
        Assert.True(generated.ResponseSent);
        Assert.False(generated.TimedOut);
        Assert.Contains("inventory-unverified", generated.Output, StringComparison.Ordinal);
        Assert.Contains("persistence-unverified", generated.Output, StringComparison.Ordinal);
        AssertCanariesAbsent(planned.Text + generated.Output + generated.Error);
        var readback = CshellsSourceReader.Read(
            File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.json")),
            File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.Production.json")),
            "default");
        Assert.Equal(new[] { "A", "C" }, readback.EnabledFeatureIds);
        var candidateFiles = Directory.GetFiles(fixture.CandidateDirectory, "*.json")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);
        Assert.Equal(source.Keys.Order(StringComparer.Ordinal), candidateFiles.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in source)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(fixture.HostDirectory, name)));
            if (name != "shells.Production.json")
                Assert.Equal(bytes, candidateFiles[name]);
        }
        foreach (var candidateFile in Directory.GetFiles(fixture.CandidateDirectory, "*.json"))
            AssertCanariesAbsent(File.ReadAllText(candidateFile));
    }

    [Fact]
    public async Task Case_distinct_workspace_profiles_are_both_parsed_and_unused_input_changes_are_rejected()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();

        var profileDirectory = Path.GetDirectoryName(fixture.OutputPath)!;
        var unusedPath = Path.Join(profileDirectory, "profile.json");
        var selectedPath = Path.Join(profileDirectory, "Profile.json");
        var unused = WriteWorkspaceProfileAtPath(unusedPath, "unused-profile", "1", ["A"]);
        if (File.Exists(selectedPath))
            return; // The current volume resolves these case-only paths to the same file.
        var selected = WriteWorkspaceProfileAtPath(selectedPath, "selected-profile", "2", ["A", "C"]);
        Assert.NotEqual(unused.Id, selected.Id);
        Assert.NotEqual(unused.Version, selected.Version);
        Assert.NotEqual(unused.Digest, selected.Digest);

        var input = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        input["profile"] = JsonSerializer.SerializeToNode(
            new DefinitionReference("workspace", "profile", selected.Id, selected.Version, selected.Digest), s_json);
        input["add"] = new JsonArray();
        input["remove"] = new JsonArray();
        File.WriteAllText(fixture.OutputPath, input.ToJsonString(s_json));

        // The selected profile is the second case-only path, so a comparer that folds Unix paths
        // would collapse it with the first (unused) profile and fail to resolve this pin.
        var accepted = await fixture.RunAcceptInteractiveAsync("accept", [unusedPath, selectedPath]);
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        using (var preview = ReadPreview(accepted.Output + accepted.Error))
        {
            Assert.Equal(new[] { "A", "C" }, Strings(preview.RootElement.GetProperty("candidateFeatureIds")));
            Assert.Equal(new[] { "A" }, Strings(preview.RootElement.GetProperty("acceptedFeatureIds")));
        }

        var acceptedDocument = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        Assert.Equal("selected-profile", acceptedDocument["profile"]!["id"]!.GetValue<string>());
        Assert.Equal(new[] { "A", "C" }, Strings(acceptedDocument["accepted"]!["featureIds"]!.AsArray()));

        File.Delete(fixture.AcceptedOutputPath);
        var changed = await fixture.RunAcceptInteractiveAsync(
            "accept", [selectedPath, unusedPath], inputToChangeAtReview: unusedPath, replacementText: "{}");
        Assert.Equal(ToolExitCode.ResolutionFailure, changed.ExitCode);
        Assert.Contains("composition-input-changed", changed.Output + changed.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
        Assert.Empty(Directory.GetFiles(profileDirectory, ".accepted.json.*.tmp"));
    }

    [Fact]
    public async Task Accept_preserves_omitted_and_explicit_null_optional_fields()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        authored.Remove("settings");
        authored["resources"] = null;
        var jsonBytes = Encoding.UTF8.GetBytes(authored.ToJsonString(s_json));
        File.WriteAllBytes(fixture.OutputPath, [.. Encoding.UTF8.GetPreamble(), .. jsonBytes]);

        var run = await fixture.RunAcceptInteractiveAsync("accept");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        var output = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        Assert.False(output.ContainsKey("settings"));
        Assert.True(output.TryGetPropertyValue("resources", out var resources));
        Assert.Null(resources);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("")]
    public async Task Declining_or_leaving_acceptance_blank_writes_no_file(string response)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();

        var run = await fixture.RunAcceptInteractiveAsync(response);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("composition-accept-review-required", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.AcceptedOutputPath)!, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Redirected_acceptance_writes_no_preview_or_file()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();

        var run = fixture.RunAcceptRedirected();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains("composition-accept-review-required", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    [Fact]
    public async Task Existing_output_and_mismatched_catalog_pin_are_refused_before_review()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        File.WriteAllText(fixture.AcceptedOutputPath, "keep-existing");
        if (!OperatingSystem.IsWindows())
        {
            var outputExists = await fixture.RunAcceptInteractiveAsync("accept");
            Assert.Equal(ToolExitCode.ResolutionFailure, outputExists.ExitCode);
            Assert.Contains("bridge-output-exists", outputExists.Output + outputExists.Error, StringComparison.Ordinal);
            Assert.Equal("keep-existing", File.ReadAllText(fixture.AcceptedOutputPath));
        }

        File.Delete(fixture.AcceptedOutputPath);
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["catalog"]!["id"] = "mismatched-catalog";
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));
        var mismatch = fixture.RunAcceptRedirected();
        Assert.Equal(ToolExitCode.ResolutionFailure, mismatch.ExitCode);
        Assert.Contains("composition-accept-blocked", mismatch.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, mismatch.Output);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));

        fixture.WriteAcceptedComposition();
        authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["accepted"]!["catalogDigest"] = new string('f', 64);
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));
        var acceptedDigestMismatch = fixture.RunAcceptRedirected();
        Assert.Equal(ToolExitCode.ResolutionFailure, acceptedDigestMismatch.ExitCode);
        Assert.Contains("composition-accept-blocked", acceptedDigestMismatch.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, acceptedDigestMismatch.Output);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));

        fixture.WriteAcceptedComposition();
        authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["profile"] = new JsonObject
        {
            ["origin"] = "foundation",
            ["kind"] = "profile",
            ["id"] = "missing-profile",
            ["version"] = "1",
            ["digest"] = new string('a', 64)
        };
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));
        var definitionMismatch = fixture.RunAcceptRedirected();
        Assert.Equal(ToolExitCode.ResolutionFailure, definitionMismatch.ExitCode);
        Assert.Contains("composition-accept-blocked", definitionMismatch.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, definitionMismatch.Output);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    [Fact]
    public void Known_missing_required_definition_dependency_blocks_acceptance_without_inventory()
    {
        using var fixture = new CompositionBridgeFixture();
        var definitionDraft = new SelectionDefinition("profile", "required-runtime", "1", new string('0', 64), ["A"],
            "Safe rationale", "Required runtime", "Profile with a reviewed required edge.",
            [new DependencyExplanation("A", "B", "required", "Safe reason")]);
        var definition = definitionDraft with { Digest = SelectionDigest.ComputeDefinitionDigest(definitionDraft) };
        var catalogDraft = new SelectionCatalog("1", "dependency-fixture", "1", "test", new string('0', 64), [definition], []);
        var catalog = catalogDraft with { Digest = SelectionDigest.ComputeCatalogDigest(catalogDraft) };
        var authored = new AuthoredComposition("1", new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            new DefinitionReference("foundation", "profile", definition.Id, definition.Version, definition.Digest),
            [], [], [], new AcceptedSelection(catalog.Digest, ["A"], []), null, null);
        File.WriteAllText(fixture.CatalogPath, JsonSerializer.Serialize(catalog, s_json));
        File.WriteAllText(fixture.OutputPath, JsonSerializer.Serialize(authored, s_json));

        var run = fixture.RunAcceptRedirected();

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("composition-accept-blocked", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    [Theory]
    [InlineData("composition")]
    [InlineData("catalog")]
    [InlineData("workspace-profile")]
    public async Task Any_supplied_input_change_during_review_cleans_staging(string inputKind)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var changedPath = inputKind switch
        {
            "composition" => fixture.OutputPath,
            "catalog" => fixture.CatalogPath,
            _ => WriteWorkspaceProfile(fixture.OutputPath)
        };
        var profilePath = inputKind == "workspace-profile" ? changedPath : null;

        var run = await fixture.RunAcceptInteractiveAsync("accept", profilePath, changedPath, "{}");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("composition-input-changed", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.AcceptedOutputPath)!, ".accepted.json.*.tmp"));
    }

    [Fact]
    public async Task Regular_file_check_refuses_directory_symlink_device_and_fifo_without_hanging()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var badDirectory = Path.Join(Path.GetDirectoryName(fixture.OutputPath)!, "profile-directory");
        Directory.CreateDirectory(badDirectory);
        AssertUnreadable(fixture, badDirectory);

        if (OperatingSystem.IsWindows())
            return;

        var symlink = Path.Join(Path.GetDirectoryName(fixture.OutputPath)!, "profile-link.json");
        File.CreateSymbolicLink(symlink, fixture.CatalogPath);
        AssertUnreadable(fixture, symlink);
        AssertUnreadable(fixture, "/dev/null");

        var fifo = Path.Join(Path.GetDirectoryName(fixture.OutputPath)!, "profile-pipe");
        using (var mkfifo = Process.Start(new ProcessStartInfo("/usr/bin/mkfifo") { ArgumentList = { fifo } }))
        {
            Assert.NotNull(mkfifo);
            Assert.True(mkfifo.WaitForExit(2_000));
            Assert.Equal(0, mkfifo.ExitCode);
        }

        var run = await PseudoTerminalCli.RunElsaAsync(
            "Type accept to write the accepted composition: ", "accept",
            ["composition", "accept", "--composition", fixture.OutputPath, "--catalog", fixture.CatalogPath,
                "--workspace-profile", fifo, "--output", fixture.AcceptedOutputPath],
            timeout: TimeSpan.FromSeconds(5));
        Assert.False(run.TimedOut);
        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("composition-input-unreadable", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    [Fact]
    public async Task Unsafe_feature_identity_is_refused_before_any_preview()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["add"] = new JsonArray("unsafe=identity");
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));

        var run = await fixture.RunAcceptInteractiveAsync("accept");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.False(run.ResponseSent);
        Assert.False(run.TimedOut);
        Assert.DoesNotContain("candidateFeatureIds", run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe=identity", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    private static void EditAddC(string compositionPath)
    {
        var authored = JsonNode.Parse(File.ReadAllText(compositionPath))!;
        authored["add"] = new JsonArray("A", "C");
        authored["remove"] = new JsonArray();
        File.WriteAllText(compositionPath, authored.ToJsonString(s_json));
    }

    private static JsonObject Lock(string featureId, string packageId) => new()
    {
        ["featureId"] = featureId,
        ["kind"] = "package",
        ["packageId"] = packageId,
        ["packageVersion"] = "1.2.3",
        ["manifestDigest"] = new string('a', 64),
        ["evidenceSource"] = "accepted-canary"
    };

    private static string WriteWorkspaceProfile(string compositionPath, string[]? members = null)
    {
        var directory = Path.GetDirectoryName(compositionPath)!;
        var path = Path.Join(directory, "workspace-profile.json");
        WriteWorkspaceProfileAtPath(path, "local-profile", "1", members ?? ["A"]);
        return path;
    }

    private static SelectionDefinition WriteWorkspaceProfileAtPath(
        string path,
        string id,
        string version,
        string[] members,
        DependencyExplanation[]? explanations = null)
    {
        var draft = new SelectionDefinition("profile", id, version, new string('0', 64), [.. members],
            Canary, "Local profile", "A pinned workspace profile.", [.. explanations ?? []]);
        var definition = draft with { Digest = SelectionDigest.ComputeDefinitionDigest(draft) };
        var profileJson = JsonSerializer.SerializeToNode(definition, s_json)!.AsObject();
        profileJson["schemaVersion"] = "1";
        File.WriteAllText(path, profileJson.ToJsonString(s_json));
        return definition;
    }

    private static void AssertUnreadable(CompositionBridgeFixture fixture, string profilePath)
    {
        var run = fixture.RunAcceptRedirected(profilePath);
        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains("composition-input-unreadable", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AcceptedOutputPath));
    }

    private static JsonDocument ReadPreview(string output)
    {
        var end = output.IndexOf("Type accept to write the accepted composition: ", StringComparison.Ordinal);
        Assert.True(end >= 0, "The interactive command did not emit its review prompt.");
        return JsonDocument.Parse(output[..end]);
    }

    private static void AssertOnlyAcceptedSelectionChanged(JsonObject input, JsonObject output)
    {
        var expected = input.DeepClone().AsObject();
        expected["accepted"]!["featureIds"] = output["accepted"]!["featureIds"]!.DeepClone();
        expected["accepted"]!["locks"] = output["accepted"]!["locks"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(expected, output));
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
    private static string[] Strings(JsonArray array) => [.. array.Select(item => item!.GetValue<string>())];
    private static string[] ReadFindingCodes(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        return [.. document.RootElement.GetProperty("findings").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)];
    }

    private static void AssertCanariesAbsent(string text)
    {
        Assert.DoesNotContain(Canary, text, StringComparison.Ordinal);
        Assert.DoesNotContain(DependencyCanary, text, StringComparison.Ordinal);
    }
}
