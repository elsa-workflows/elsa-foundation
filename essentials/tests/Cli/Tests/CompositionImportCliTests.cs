using System.Text.Json;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Json;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionImportCliTests
{
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";

    [Fact]
    public async Task Import_accepts_a_redacted_preview_and_writes_only_reviewed_portable_intent()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();
        var sourceBefore = Directory.GetFiles(fixture.HostDirectory, "*.json")
            .ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes, StringComparer.Ordinal);

        var run = await fixture.RunImportInteractiveAsync("accept");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.True(run.ResponseSent);
        Assert.False(run.TimedOut);
        AssertRedacted(run.Output + run.Error);
        Assert.True(File.Exists(fixture.OutputPath));
        var authoredJson = File.ReadAllText(fixture.OutputPath);
        AssertRedacted(authoredJson);
        var authored = SelectionJsonReader.ParseComposition(authoredJson);
        Assert.Null(authored.Profile);
        Assert.Empty(authored.Groups);
        Assert.Equal(new[] { "A" }, authored.Add.ToArray());
        Assert.Equal(new[] { "B" }, authored.Remove.ToArray());
        Assert.Equal(new[] { "A" }, authored.Accepted.FeatureIds.ToArray());
        Assert.Empty(authored.Accepted.Locks);
        Assert.Equal("primary", authored.Resources?.GetProperty("persistence").GetProperty("defaultResource").GetString());
        Assert.Equal("primary", authored.Resources?.GetProperty("persistence").GetProperty("bindings").GetProperty("A").GetString());
        Assert.Equal(JsonValueKind.True, authored.Settings?.GetProperty("A").GetProperty("Flag").ValueKind);
        Assert.Equal(0, authored.Settings?.GetProperty("A").GetProperty("Limit").GetInt32());
        Assert.DoesNotContain("Future", authoredJson, StringComparison.Ordinal);
        Assert.Contains("\"masked\": true", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"sourceLayer\": \"overlay\"", run.Output, StringComparison.Ordinal);

        foreach (var (name, bytes) in sourceBefore)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(fixture.HostDirectory, name)));

        var plan = DotnetElsa.Run("composition", "plan", "--catalog", fixture.CatalogPath,
            "--composition", fixture.OutputPath, "--format", "json");
        Assert.Equal(ToolExitCode.Success, plan.ExitCode);
        AssertRedacted(plan.Text);
    }

    [Fact]
    public async Task Import_decline_publishes_nothing()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CompositionBridgeFixture();

        var run = await fixture.RunImportInteractiveAsync("decline");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run.Output + run.Error);
    }

    [Fact]
    public void Import_requires_an_interactive_review_and_publishes_nothing_when_stdin_is_redirected()
    {
        using var fixture = new CompositionBridgeFixture();

        var run = fixture.RunImport();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run);
    }

    [Fact]
    public void Import_refuses_a_missing_named_overlay_before_publication()
    {
        using var fixture = new CompositionBridgeFixture();

        var run = fixture.RunImport(environment: "Absent");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-source-missing", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run);
    }

    [Fact]
    public void Import_refuses_a_catalog_digest_mismatch_without_disclosing_source_values()
    {
        using var fixture = new CompositionBridgeFixture();
        File.WriteAllText(fixture.CatalogPath, fixture.CatalogJson.Replace("\"digest\":\"", "\"digest\":\"f", StringComparison.Ordinal));

        var run = fixture.RunImport();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run);
    }

    private static void AssertRedacted(CliRun run) => AssertRedacted(run.Text);

    private static void AssertRedacted(string text)
    {
        Assert.DoesNotContain(ConnectionCanary, text, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownCanary, text, StringComparison.Ordinal);
    }

}
