using System.Text.Json;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionImportCliTests
{
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task Import_accepts_a_redacted_preview_and_writes_only_reviewed_portable_intent()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new ImportFiles();
        var sourceBefore = Directory.GetFiles(fixture.HostDirectory, "*.json")
            .ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes, StringComparer.Ordinal);

        var run = await fixture.RunInteractiveAsync("accept");

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

        using var fixture = new ImportFiles();

        var run = await fixture.RunInteractiveAsync("decline");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Output + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run.Output + run.Error);
    }

    [Fact]
    public void Import_requires_an_interactive_review_and_publishes_nothing_when_stdin_is_redirected()
    {
        using var fixture = new ImportFiles();

        var run = fixture.Run();

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("bridge-review-required", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run);
    }

    [Fact]
    public void Import_refuses_a_missing_named_overlay_before_publication()
    {
        using var fixture = new ImportFiles();

        var run = fixture.Run(environment: "Absent");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-source-missing", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath));
        AssertRedacted(run);
    }

    [Fact]
    public void Import_refuses_a_catalog_digest_mismatch_without_disclosing_source_values()
    {
        using var fixture = new ImportFiles();
        File.WriteAllText(fixture.CatalogPath, fixture.CatalogJson.Replace("\"digest\":\"", "\"digest\":\"f", StringComparison.Ordinal));

        var run = fixture.Run();

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

    private sealed class ImportFiles : IDisposable
    {
        private readonly TempDirectory _directory = new("elsa-composition-import-");

        public ImportFiles()
        {
            HostDirectory = _directory.File("host");
            Directory.CreateDirectory(HostDirectory);
            var fixtureDirectory = Path.Join(RepoRoot, "tests", "essentials", "Cli", "Tests", "Fixtures", "CompositionBridge");
            foreach (var file in Directory.GetFiles(fixtureDirectory, "*.json"))
                File.Copy(file, Path.Join(HostDirectory, Path.GetFileName(file)));

            var draft = new SelectionCatalog("1", "bridge-test", "1", "elsa-foundation", new string('0', 64), [], []);
            var catalog = draft with { Digest = SelectionDigest.ComputeCatalogDigest(draft) };
            CatalogJson = JsonSerializer.Serialize(catalog, s_json);
            CatalogPath = _directory.File("catalog.json");
            ReviewPath = _directory.File("review.json");
            OutputPath = _directory.File("authored.json");
            File.WriteAllText(CatalogPath, CatalogJson);
            File.WriteAllText(ReviewPath,
                "{\"schemaVersion\":\"1\",\"fields\":[{" +
                "\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true},{" +
                "\"featureId\":\"A\",\"pointer\":\"/Limit\",\"type\":\"number\",\"portable\":true}]}");
        }

        public string HostDirectory { get; }
        public string CatalogPath { get; }
        public string CatalogJson { get; }
        public string ReviewPath { get; }
        public string OutputPath { get; }

        public CliRun Run(string environment = "Production") => DotnetElsa.RunWithStdinAsync(string.Empty,
            "composition", "import", "--host-dir", HostDirectory, "--shell", "default",
            "--environment", environment, "--catalog", CatalogPath,
            "--setting-review", ReviewPath, "--output", OutputPath).GetAwaiter().GetResult();

        public Task<PseudoTerminalCliRun> RunInteractiveAsync(string response) => PseudoTerminalCli.RunElsaAsync(
            "Type accept to write the authored composition: ",
            response,
            ["composition", "import", "--host-dir", HostDirectory, "--shell", "default",
                "--environment", "Production", "--catalog", CatalogPath,
                "--setting-review", ReviewPath, "--output", OutputPath]);

        public void Dispose() => _directory.Dispose();

        private static string RepoRoot
        {
            get
            {
                for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                    if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                        return directory.FullName;
                throw new DirectoryNotFoundException("Could not locate the repository root.");
            }
        }
    }
}
