using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionAcceptCliTests
{
    private const string Canary = "COMPOSITION_ACCEPT_OPAQUE_CANARY_NOT_A_SECRET";
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
        var stale = RunGenerate(fixture, fixture.OutputPath, staleCandidate);
        Assert.Equal(ToolExitCode.Refusal, stale.ExitCode);
        Assert.Contains("bridge-selection-drift", stale.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(staleCandidate));

        var accepted = await fixture.RunAcceptInteractiveAsync("accept");
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        Assert.True(accepted.ResponseSent);
        Assert.False(accepted.TimedOut);
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
        var generated = await RunGenerateInteractiveAsync(fixture, fixture.AcceptedOutputPath, candidate);
        Assert.Equal(ToolExitCode.Success, generated.ExitCode);
        Assert.True(Directory.Exists(candidate));
        var readback = CshellsSourceReader.Read(
            File.ReadAllText(Path.Join(candidate, "shells.json")),
            File.ReadAllText(Path.Join(candidate, "shells.Production.json")), "default");
        Assert.Equal(new[] { "A", "C" }, readback.EnabledFeatureIds);
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
        accepted["featureIds"] = new JsonArray("A", "B");
        accepted["locks"] = new JsonArray(Lock("A", "package-a-" + Canary), Lock("B", "package-b"));
        authored["settings"]!["opaque"] = new JsonObject { ["nested"] = Canary };
        authored["resources"]!["opaque"] = Canary;
        var settings = authored["settings"]!.DeepClone();
        var resources = authored["resources"]!.DeepClone();
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));

        var run = await fixture.RunAcceptInteractiveAsync("accept");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.DoesNotContain(Canary, run.Output + run.Error, StringComparison.Ordinal);
        using var preview = ReadPreview(run.Output + run.Error);
        Assert.Equal(new[] { "A" }, Strings(preview.RootElement.GetProperty("retainedLockFeatureIds")));
        Assert.Equal(new[] { "B" }, Strings(preview.RootElement.GetProperty("droppedLockFeatureIds")));
        Assert.Equal(new[] { "B" }, Strings(preview.RootElement.GetProperty("removedFeatureIds")));

        var output = JsonNode.Parse(File.ReadAllText(fixture.AcceptedOutputPath))!.AsObject();
        Assert.True(JsonNode.DeepEquals(settings, output["settings"]));
        Assert.True(JsonNode.DeepEquals(resources, output["resources"]));
        Assert.Equal(new[] { "A" }, Strings(output["accepted"]!["featureIds"]!.AsArray()));
        Assert.Equal("package-a-" + Canary, output["accepted"]!["locks"]![0]!["packageId"]!.GetValue<string>());
        Assert.Single(output["accepted"]!["locks"]!.AsArray());
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
        Assert.True(output.ContainsKey("resources"));
        Assert.Null(output["resources"]);
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

    private static string WriteWorkspaceProfile(string compositionPath)
    {
        var directory = Path.GetDirectoryName(compositionPath)!;
        var path = Path.Join(directory, "workspace-profile.json");
        var draft = new SelectionDefinition("profile", "unused", "1", new string('0', 64), ["A"],
            "Safe rationale", "Unused profile", "A valid but unused workspace profile.", []);
        var definition = draft with { Digest = SelectionDigest.ComputeDefinitionDigest(draft) };
        var profileJson = JsonSerializer.SerializeToNode(definition, s_json)!.AsObject();
        profileJson["schemaVersion"] = "1";
        File.WriteAllText(path, profileJson.ToJsonString(s_json));
        return path;
    }

    private static CliRun RunGenerate(CompositionBridgeFixture fixture, string composition, string outputDirectory) =>
        DotnetElsa.Run("composition", "generate", "--host-dir", fixture.HostDirectory,
            "--shell", "default", "--environment", "Production", "--catalog", fixture.CatalogPath,
            "--composition", composition, "--setting-review", fixture.ReviewPath, "--output-dir", outputDirectory);

    private static Task<PseudoTerminalCliRun> RunGenerateInteractiveAsync(
        CompositionBridgeFixture fixture, string composition, string outputDirectory) =>
        PseudoTerminalCli.RunElsaAsync("Type generate to write the candidate: ", "generate",
            ["composition", "generate", "--host-dir", fixture.HostDirectory,
                "--shell", "default", "--environment", "Production", "--catalog", fixture.CatalogPath,
                "--composition", composition, "--setting-review", fixture.ReviewPath, "--output-dir", outputDirectory]);

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
        var depth = 0;
        var inString = false;
        var escaped = false;
        var start = output.IndexOf('{');
        Assert.True(start >= 0, "The interactive command did not emit its review preview.");
        for (var index = start; index < output.Length; index++)
        {
            var character = output[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    inString = false;
                continue;
            }

            if (character == '"')
                inString = true;
            else if (character == '{')
                depth++;
            else if (character == '}' && --depth == 0)
                return JsonDocument.Parse(output[start..(index + 1)]);
        }

        throw new Xunit.Sdk.XunitException("The interactive command emitted an incomplete review preview.");
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
    private static string[] Strings(JsonArray array) => [.. array.Select(item => item!.GetValue<string>())];
    private static string[] ReadFindingCodes(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        return [.. document.RootElement.GetProperty("findings").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)];
    }
}
