using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Cli.Tests;

/// <summary>Disposable two-shell host files and reviewed inputs shared by import and generation tests.</summary>
internal sealed class CompositionBridgeFixture : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly TempDirectory _directory = new("elsa-composition-bridge-");

    public CompositionBridgeFixture()
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
        CandidateDirectory = _directory.File("candidate");
        File.WriteAllText(CatalogPath, CatalogJson);
        File.WriteAllText(ReviewPath, """
            {"schemaVersion":"1","fields":[
              {"featureId":"A","pointer":"/Flag","type":"boolean","portable":true},
              {"featureId":"A","pointer":"/Limit","type":"number","portable":true}
            ]}
            """);
    }

    public string HostDirectory { get; }
    public string CatalogPath { get; }
    public string CatalogJson { get; }
    public string ReviewPath { get; }
    public string OutputPath { get; }
    public string CandidateDirectory { get; }

    public CliRun RunImport(string environment = "Production") => DotnetElsa.RunWithStdinAsync(string.Empty,
        "composition", "import", "--host-dir", HostDirectory, "--shell", "default",
        "--environment", environment, "--catalog", CatalogPath,
        "--setting-review", ReviewPath, "--output", OutputPath).GetAwaiter().GetResult();

    public Task<PseudoTerminalCliRun> RunImportInteractiveAsync(string response) => PseudoTerminalCli.RunElsaAsync(
        "Type accept to write the authored composition: ", response,
        ["composition", "import", "--host-dir", HostDirectory, "--shell", "default",
            "--environment", "Production", "--catalog", CatalogPath,
            "--setting-review", ReviewPath, "--output", OutputPath]);

    public CliRun RunGenerate(string environment = "Production") => DotnetElsa.RunWithStdinAsync(string.Empty,
        "composition", "generate", "--host-dir", HostDirectory, "--shell", "default",
        "--environment", environment, "--catalog", CatalogPath,
        "--composition", OutputPath, "--setting-review", ReviewPath,
        "--output-dir", CandidateDirectory).GetAwaiter().GetResult();

    public Task<PseudoTerminalCliRun> RunGenerateInteractiveAsync(
        string response,
        string? sourceFileToChangeAtReview = null,
        string? outputDirectory = null) => PseudoTerminalCli.RunElsaAsync(
        "Type generate to write the candidate: ", response,
        ["composition", "generate", "--host-dir", HostDirectory, "--shell", "default",
            "--environment", "Production", "--catalog", CatalogPath,
            "--composition", OutputPath, "--setting-review", ReviewPath,
            "--output-dir", outputDirectory ?? CandidateDirectory],
        beforeResponsePath: sourceFileToChangeAtReview is null ? null : Path.Join(HostDirectory, sourceFileToChangeAtReview),
        beforeResponseText: sourceFileToChangeAtReview is null ? null : "{}");

    public void WriteAcceptedComposition(int limit = 0)
    {
        var source = CompositionFileSource.Open(HostDirectory, "default", "Production").Snapshot;
        var imported = CompositionImporter.Import(
            source.ReadText("shells.json"),
            source.ReadText(source.Selection.ShellOverlayFileName),
            source.ReadText("appsettings.json"),
            source.Selection.AppsettingsOverlayFileName is { } appOverlay ? source.ReadText(appOverlay) : null,
            "default", "Production",
            SelectionJsonReader.ParseCatalog(CatalogJson),
            SettingReviewReader.Parse(File.ReadAllText(ReviewPath)));
        var authored = JsonNode.Parse(JsonSerializer.Serialize(imported.Authored, s_json))!;
        authored["settings"]!["A"]!["Limit"] = limit;
        File.WriteAllText(OutputPath, authored.ToJsonString(s_json));
    }

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
