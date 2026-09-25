using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionGenerateCliTests
{
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";

    [Fact]
    public async Task Approved_generation_changes_only_the_reviewed_existing_base_field_and_preserves_every_source()
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
}
