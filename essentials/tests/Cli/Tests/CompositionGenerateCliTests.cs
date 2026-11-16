using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionGenerateCliTests
{
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";
    private const string ProfileInputCanary = "COMPOSITION_PROFILE_INPUT_CANARY_NOT_A_SECRET";
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Approved_generation_changes_the_reviewed_field_and_materializes_explicit_removal_without_source_writes()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);
        var original = Directory.GetFiles(fixture.HostDirectory, "*.json")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);

        var run = await fixture.RunGenerateInteractiveAsync("generate");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.True(run.ResponseSent);
        Assert.False(run.TimedOut);
        AssertRedacted(run.Output + run.Error);
        Assert.Contains("\"pointer\": \"/Limit\"", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"sourceLayer\": \"base\"", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"changed\": true", run.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.CandidateDirectory));
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal),
            Directory.GetFiles(fixture.CandidateDirectory, "*.json")
                .Select(Path.GetFileName).Order(StringComparer.Ordinal));

        foreach (var (name, bytes) in original)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(fixture.HostDirectory, name)));
            var candidate = File.ReadAllBytes(Path.Join(fixture.CandidateDirectory, name));
            if (name == "shells.json")
            {
                var expected = JsonNode.Parse(bytes)!;
                expected["CShells"]!["Shells"]!["default"]!["Features"]!["A"]!["Limit"] = 1;
                Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(candidate)));
            }
            else if (name == "shells.Production.json")
            {
                // Import records base-disabled B as an explicit removal. The selected overlay must
                // retain that intent even if a declared host composer later supplies B as a default.
                var expected = JsonNode.Parse(bytes)!;
                expected["CShells"]!["Shells"]!["default"]!["Features"]!["B"] = false;
                Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(candidate)));
            }
            else
                Assert.Equal(bytes, candidate);
        }
        Assert.Contains(ConnectionCanary, File.ReadAllText(Path.Join(fixture.CandidateDirectory, "appsettings.json")), StringComparison.Ordinal);
        Assert.Contains(UnknownCanary, File.ReadAllText(Path.Join(fixture.CandidateDirectory, "shells.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approved_generation_can_emit_a_complete_secret_safe_handoff()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = await fixture.RunGenerateInteractiveAsync("generate", handoffHost: "workbench-a");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        var line = Assert.Single(run.Output.Split('\n'), item => item.StartsWith("{\"handoff\":", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(line);
        var handoff = document.RootElement.GetProperty("handoff");
        Assert.True(Guid.TryParseExact(handoff.GetProperty("candidateId").GetString(), "N", out _));
        Assert.Equal("workbench-a", handoff.GetProperty("host").GetString());
        Assert.Equal("default", handoff.GetProperty("shell").GetString());
        Assert.Equal("Production", handoff.GetProperty("environment").GetString());
        Assert.Equal(6, handoff.GetProperty("includedFiles").GetArrayLength());
        Assert.Equal("external-attestation-required", handoff.GetProperty("deploymentIntegrity").GetString());
        Assert.Equal("unchecked", handoff.GetProperty("activation").GetString());
        Assert.True(Directory.Exists(fixture.CandidateDirectory));
        Assert.DoesNotContain(fixture.HostDirectory, run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("shells.Staging.json", line, StringComparison.Ordinal);
        AssertRedacted(run.Output + run.Error);
    }

    [Fact]
    public async Task Unsafe_handoff_host_refuses_before_candidate_publication()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = await fixture.RunGenerateInteractiveAsync("generate", handoffHost: "../host");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("candidate-incomplete", run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("\"handoff\":", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Output + run.Error);
    }

    [Theory]
    [InlineData("shells.Production.json")]
    [InlineData("shells.Staging.json")]
    public async Task Changed_selected_or_unselected_source_refuses_handoff_without_success(string fileName)
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = await fixture.RunGenerateInteractiveAsync("generate", sourceFileToChangeAtReview: fileName, handoffHost: "workbench-a");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-source-changed", run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("\"handoff\":", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Output + run.Error);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("")]
    public async Task Declined_or_cancelled_diff_publishes_nothing(string response)
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = await fixture.RunGenerateInteractiveAsync(response, handoffHost: "workbench-a");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("\"handoff\":", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Output + run.Error);
    }

    [Fact]
    public void Noninteractive_generation_requires_review_and_publishes_nothing()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = fixture.RunGenerate();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Text);
    }

    [Fact]
    public void Unreviewed_new_field_is_refused_before_diff_or_publication()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["settings"]!["A"]!["Unreviewed"] = "opaque-not-a-secret";
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        var run = fixture.RunGenerate();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque-not-a-secret", run.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Text);
    }

    [Fact]
    public async Task Existing_candidate_directory_is_not_overwritten()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);
        Directory.CreateDirectory(fixture.CandidateDirectory);
        var marker = Path.Join(fixture.CandidateDirectory, "marker.txt");
        File.WriteAllText(marker, "existing");

        var run = await fixture.RunGenerateInteractiveAsync("generate");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-output-exists", run.Output + run.Error, StringComparison.Ordinal);
        Assert.Equal("existing", File.ReadAllText(marker));
        AssertRedacted(run.Output + run.Error);
    }

    [Theory]
    [InlineData("shells.Production.json")]
    [InlineData("shells.Staging.json")]
    public async Task Source_changed_after_diff_review_refuses_without_publishing(string fileName)
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);

        var run = await fixture.RunGenerateInteractiveAsync("generate", sourceFileToChangeAtReview: fileName);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.True(run.ResponseSent);
        Assert.Contains("bridge-source-changed", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        AssertRedacted(run.Output + run.Error);
    }

    [Theory]
    [InlineData("composition")]
    [InlineData("catalog")]
    [InlineData("setting-review")]
    [InlineData("selected-profile")]
    [InlineData("unused-profile")]
    public async Task Any_supplied_input_change_after_review_refuses_and_cleans_staging(string inputKind)
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        var setup = PrepareWorkspaceGeneration(fixture);
        var changedPath = inputKind switch
        {
            "composition" => fixture.OutputPath,
            "catalog" => fixture.CatalogPath,
            "setting-review" => fixture.ReviewPath,
            "selected-profile" => setup.SelectedPath,
            _ => setup.UnusedPath
        };

        var run = await fixture.RunGenerateInteractiveAsync(
            "generate",
            workspaceProfilePaths: setup.Paths,
            inputToChangeAtReview: changedPath,
            replacementText: "{}");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.True(run.ResponseSent);
        Assert.Contains("composition-input-changed", run.Output + run.Error, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);
        AssertRedacted(run.Output + run.Error);
    }

    [Fact]
    public void Missing_workspace_profile_argument_or_file_refuses_without_output()
    {
        using var fixture = new CompositionBridgeFixture();
        var setup = PrepareWorkspaceGeneration(fixture);

        var missingArgument = fixture.RunGenerate(workspaceProfilePaths: [setup.UnusedPath]);
        Assert.Equal(ToolExitCode.Refusal, missingArgument.ExitCode);
        Assert.Contains("bridge-selection-drift", missingArgument.Error, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);

        var missingPath = Path.Join(Path.GetDirectoryName(setup.SelectedPath)!, $"missing-{ProfileInputCanary}.json");
        var missingFile = fixture.RunGenerate(workspaceProfilePaths: [setup.UnusedPath, missingPath]);
        Assert.Equal(ToolExitCode.ResolutionFailure, missingFile.ExitCode);
        Assert.Contains("composition-input-unreadable", missingFile.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ProfileInputCanary, missingFile.Text, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);
        AssertRedacted(missingArgument.Text + missingFile.Text);
    }

    [Fact]
    public void Mismatched_valid_workspace_profile_refuses_without_output()
    {
        using var fixture = new CompositionBridgeFixture();
        var setup = PrepareWorkspaceGeneration(fixture);
        var mismatch = fixture.WriteWorkspaceProfile("mismatched-profile.json", "selected-profile", "3", ["A", "C"]);

        var run = fixture.RunGenerate(workspaceProfilePaths: [setup.UnusedPath, mismatch.Path]);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-selection-drift", run.Error, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);
        AssertRedacted(run.Text);
    }

    [Fact]
    public void Workspace_profile_missing_a_reviewed_required_dependency_refuses_without_output()
    {
        using var fixture = new CompositionBridgeFixture();
        var setup = PrepareWorkspaceGeneration(fixture,
            [new DependencyExplanation("A", "D", "required", ProfileInputCanary)]);

        var run = fixture.RunGenerate(workspaceProfilePaths: setup.Paths);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-required-dependency-missing", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ProfileInputCanary, run.Text, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);
        AssertRedacted(run.Text);
    }

    [Theory]
    [InlineData("malformed", "bridge-source-invalid")]
    [InlineData("digest-invalid", "bridge-source-invalid")]
    [InlineData("duplicate-same-path", "bridge-authored-invalid")]
    [InlineData("duplicate-distinct-files", "bridge-authored-invalid")]
    public void Invalid_or_duplicate_workspace_profile_snapshots_refuse_without_output(string inputKind, string expectedCode)
    {
        using var fixture = new CompositionBridgeFixture();
        var setup = PrepareWorkspaceGeneration(fixture);
        IReadOnlyList<string> profiles;
        switch (inputKind)
        {
            case "malformed":
                var malformedPath = Path.Join(
                    Path.GetDirectoryName(setup.SelectedPath)!,
                    $"malformed-{ProfileInputCanary}.json");
                File.WriteAllText(malformedPath, $$"""{"payload":"{{ProfileInputCanary}}"}""");
                profiles = [setup.UnusedPath, malformedPath];
                break;
            case "digest-invalid":
                var profile = JsonNode.Parse(File.ReadAllText(setup.SelectedPath))!;
                profile["digest"] = new string('f', 64);
                File.WriteAllText(setup.SelectedPath, profile.ToJsonString(s_json));
                profiles = setup.Paths;
                break;
            case "duplicate-same-path":
                profiles = [setup.UnusedPath, setup.SelectedPath, setup.SelectedPath];
                break;
            default:
                var copyPath = Path.Join(Path.GetDirectoryName(setup.SelectedPath)!, "selected-profile-copy.json");
                File.Copy(setup.SelectedPath, copyPath);
                profiles = [setup.UnusedPath, setup.SelectedPath, copyPath];
                break;
        }

        var run = fixture.RunGenerate(workspaceProfilePaths: profiles);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains(expectedCode, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ProfileInputCanary, run.Text, StringComparison.Ordinal);
        AssertNoCandidateOrStaging(fixture);
        AssertRedacted(run.Text);
    }

    [Fact]
    public async Task Missing_destination_parent_refuses_without_partial_candidate()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 1);
        var output = Path.Join(Path.GetDirectoryName(fixture.CandidateDirectory)!, "missing-parent", "candidate");

        var run = await fixture.RunGenerateInteractiveAsync("generate", outputDirectory: output);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-output-failed", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        AssertRedacted(run.Output + run.Error);
    }

    private static void AssertRedacted(string text)
    {
        Assert.DoesNotContain(ConnectionCanary, text, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownCanary, text, StringComparison.Ordinal);
    }

    private static WorkspaceGenerationSetup PrepareWorkspaceGeneration(
        CompositionBridgeFixture fixture,
        IReadOnlyList<DependencyExplanation>? explanations = null)
    {
        fixture.WriteAcceptedComposition();
        var unused = fixture.WriteWorkspaceProfile("unused-profile.json", "unused-profile", "1", ["A"]);
        var selected = fixture.WriteWorkspaceProfile("selected-profile.json", "selected-profile", "2", ["A", "C"],
            explanations: explanations);
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!.AsObject();
        authored["profile"] = JsonSerializer.SerializeToNode(
            new DefinitionReference("workspace", "profile", selected.Definition.Id, selected.Definition.Version, selected.Definition.Digest),
            s_json);
        authored["add"] = new JsonArray();
        authored["remove"] = new JsonArray();
        authored["accepted"]!["featureIds"] = new JsonArray("A", "C");
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString(s_json));
        return new WorkspaceGenerationSetup(unused.Path, selected.Path);
    }

    private static void AssertNoCandidateOrStaging(CompositionBridgeFixture fixture)
    {
        Assert.False(Directory.Exists(fixture.CandidateDirectory));
        var parent = Path.GetDirectoryName(fixture.CandidateDirectory)!;
        var name = Path.GetFileName(fixture.CandidateDirectory);
        Assert.Empty(Directory.GetFileSystemEntries(parent, $".{name}.*.tmp"));
    }

    private sealed record WorkspaceGenerationSetup(string UnusedPath, string SelectedPath)
    {
        public string[] Paths => [UnusedPath, SelectedPath];
    }
}
