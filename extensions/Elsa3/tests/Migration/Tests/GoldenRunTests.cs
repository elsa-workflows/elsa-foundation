using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Elsa3.Migration.Tests.Support;

namespace Elsa3.Migration.Tests;

/// <summary>
/// Spec 1237 T021 and T034: the fixture's <c>inventory</c> report equals the committed golden file (after
/// <c>tool.version</c> is normalized), validates against the contract schema, and a second run is byte-identical,
/// <c>migrated/</c> included (FR-022, SC-006). The built CLI gives the same report (SC-003).
/// </summary>
/// <remarks>Set <c>ELSA3_MIGRATION_UPDATE_GOLDEN=1</c> to rewrite the golden file from a run, then review its diff.</remarks>
[Collection(nameof(CompositionCollection))]
public sealed class GoldenRunTests
{
    private const string NormalizedVersion = "normalized";

    [Fact]
    public async Task The_fixture_inventory_report_equals_the_golden_file_and_a_second_run_is_byte_identical()
    {
        using var host = new TestHostFolder();

        var (first, firstOutput) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "first"));
        var (second, secondOutput) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "second"));

        Assert.Equal(1, first.ExitCode);
        Assert.Equal(1, second.ExitCode);
        var report = File.ReadAllBytes(Path.Combine(firstOutput, "report.json"));
        ReportRenderingTests.AssertValid(report);

        var normalized = Normalize(report);
        var golden = GoldenPath();
        if (Environment.GetEnvironmentVariable("ELSA3_MIGRATION_UPDATE_GOLDEN") == "1")
            File.WriteAllText(golden, normalized);
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), normalized);

        Assert.Equal(Snapshot(firstOutput), Snapshot(secondOutput));
    }

    [Fact]
    public async Task The_built_CLI_with_a_relative_out_gives_the_same_report_and_leaves_nothing_else()
    {
        using var host = new TestHostFolder();
        var (_, inProcessOutput) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "in-process"));
        var workingFolder = Path.Combine(host.Root, "cli");
        Directory.CreateDirectory(workingFolder);
        var hostData = Path.Combine(Cli.CliHostWiring.Create().HostProjectFolder, "data");
        var hostDataExisted = Directory.Exists(hostData);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Elsa3.Migration.Cli.dll"), "workflows", "inventory", FixtureRun.ExportFolder, "--out", "out" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(15));

        Assert.True(process.ExitCode == 1, $"exit {process.ExitCode}: {await standardError}");
        Assert.Equal("out", (await standardOutput).Trim().Split('\n')[^1].Trim());
        Assert.Equal(["out"], Directory.EnumerateFileSystemEntries(workingFolder).Select(Path.GetFileName));
        Assert.Equal(
            Normalize(File.ReadAllBytes(Path.Combine(inProcessOutput, "report.json"))),
            Normalize(File.ReadAllBytes(Path.Combine(workingFolder, "out", "report.json"))));
        Assert.Equal(hostDataExisted, Directory.Exists(hostData));
    }

    /// <summary>The report with <c>tool.version</c> normalized, as indented text with '\n' line ends.</summary>
    private static string Normalize(byte[] report)
    {
        var node = JsonNode.Parse(report)!;
        node["tool"]!["version"] = NormalizedVersion;
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Every file under the output folder (staging excluded) with its bytes, as relative path and content hash.</summary>
    private static string[] Snapshot(string output) =>
        Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(file => $"{Path.GetRelativePath(output, file).Replace('\\', '/')}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "Fixtures", "golden", "report.json");
}
