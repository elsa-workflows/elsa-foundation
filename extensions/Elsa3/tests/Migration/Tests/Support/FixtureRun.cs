using System.Text.Json;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests.Support;

/// <summary>One in-process run of the pipeline over the fixture export (spec 1237 T018, T021), on a temporary host folder.</summary>
internal static class FixtureRun
{
    public static string FixturesRoot { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static string ExportFolder { get; } = Path.Combine(FixturesRoot, "export");
    public static string ExportZip { get; } = Path.Combine(FixturesRoot, "export.zip");

    public static async Task<(MigrationRunResult Result, string OutputFolder)> RunAsync(
        TestHostFolder host,
        MigrationCommand command = MigrationCommand.Inventory,
        string? export = null,
        bool allVersions = false,
        string? output = null)
    {
        output ??= Path.Combine(host.Root, "out");
        var request = new MigrationRequest(command, export ?? ExportFolder, output, allVersions, "0.0.0-test");
        var result = await Elsa3MigrationRun.RunAsync(request, host.Wiring, CancellationToken.None);
        return (result, output);
    }

    public static JsonDocument ReadReport(string outputFolder) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(outputFolder, "report.json")));
}
