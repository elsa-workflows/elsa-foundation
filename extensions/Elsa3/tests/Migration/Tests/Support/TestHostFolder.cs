using Elsa3.Migration.Cli;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests.Support;

/// <summary>
/// A temporary copy of the migration host's committed settings files, used as the host folder through the internal seam
/// (spec 1237 T009), so an engineer's git-ignored <c>shells.Local.json</c> in the host project never reaches a test.
/// The host's user secrets (its <c>UserSecretsId</c>) still reach <c>assess</c>, as they do for the host's own tests: a secret
/// that lists a feature or changes a connection setting on a developer's machine can change an <c>assess</c> test's outcome.
/// </summary>
internal sealed class TestHostFolder : IDisposable
{
    private static readonly string[] CommittedFiles = ["appsettings.json", "appsettings.Development.json", "shells.json"];

    public TestHostFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "elsa3-migration-tests", Guid.NewGuid().ToString("N"));
        HostFolder = Path.Combine(Root, "host");
        Directory.CreateDirectory(HostFolder);
        var wiring = CliHostWiring.Create();
        foreach (var file in CommittedFiles)
            File.Copy(Path.Combine(wiring.HostProjectFolder, file), Path.Combine(HostFolder, file));
        Wiring = wiring with { HostProjectFolder = HostFolder };
    }

    /// <summary>The temporary root; the host folder and any test output live under it.</summary>
    public string Root { get; }

    public string HostFolder { get; }

    /// <summary>The CLI's wiring with the temporary host folder.</summary>
    public HostWiring Wiring { get; private set; }

    /// <summary>Uses a copy of the CLI's addition list with <paramref name="features"/> added, in a temporary CLI folder.</summary>
    public void AddToAdditionList(params string[] features)
    {
        var cliFolder = Path.Combine(Root, "cli");
        Directory.CreateDirectory(cliFolder);
        var source = Path.Combine(CliHostWiring.Create().CliProjectFolder, MigrationComposition.AdditionListFileName);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(source))!;
        var listed = json["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        foreach (var feature in features)
            listed[feature] = new System.Text.Json.Nodes.JsonObject();
        File.WriteAllText(Path.Combine(cliFolder, MigrationComposition.AdditionListFileName), json.ToJsonString());
        Wiring = Wiring with { CliProjectFolder = cliFolder };
    }

    /// <summary>Writes the host folder's <c>shells.Local.json</c>.</summary>
    public void WriteLocalSettings(string json) => File.WriteAllText(Path.Combine(HostFolder, "shells.Local.json"), json);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
