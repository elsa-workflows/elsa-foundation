#if ETL_V1_FIXTURE_CAPTURE
using System.Diagnostics;
using System.Security.Cryptography;
using Elsa.Etl.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    private sealed record CurrentEtlCaptureWriter(string Name, string Sha256, string ProductVersion);

    private static async Task BackupCurrentEtlDatabaseAsync(string source, string destination)
    {
        Assert.False(File.Exists(destination));
        await using var input = new SqliteConnection($"Data Source={source};Pooling=False");
        await using var output = new SqliteConnection($"Data Source={destination};Pooling=False");
        await input.OpenAsync();
        await output.OpenAsync();
        input.BackupDatabase(output);
    }

    private static CurrentEtlCaptureWriter[] CurrentEtlCaptureWriters(string directory, string source)
    {
        var writers = Directory.EnumerateFiles(directory, "Elsa.*.dll").Order(StringComparer.Ordinal).Select(path =>
        {
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? string.Empty;
            Assert.EndsWith("+" + source, version);
            return new CurrentEtlCaptureWriter(Path.GetFileName(path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), version);
        }).ToArray();
        foreach (var required in new[] { "Elsa.Etl.Core.dll", "Elsa.Etl.Runtime.dll", "Elsa.Etl.Persistence.EntityFrameworkCore.dll",
                     "Elsa.Serialization.Core.dll", "Elsa.Workflows.Runtime.Core.dll", "Elsa.Activities.Runtime.Core.dll" })
            Assert.Contains(writers, writer => writer.Name == required);
        return writers;
    }

    private static async Task<string> CurrentEtlCaptureSourceAsync()
    {
        var root = EtlV1CompatibilityFixture.FindRepositoryRoot();
        Assert.Equal(string.Empty, await GitAsync("status", "--porcelain"));
        var source = await GitAsync("rev-parse", "HEAD");
        Assert.Matches("^[0-9a-f]{40}$", source);
        return source;

        async Task<string> GitAsync(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
            return output.Trim();
        }
    }
}
#endif
