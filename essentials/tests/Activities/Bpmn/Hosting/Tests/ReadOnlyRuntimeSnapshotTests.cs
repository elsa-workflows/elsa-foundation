using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class ReadOnlyRuntimeSnapshotTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Recognized_optional_projection_shapes_preserve_the_checked_capture(
        bool claimDelivery, bool standaloneSource, bool lowerCase)
    {
        using var snapshot = await Snapshot.CreateAsync(claimDelivery, standaloneSource ? 4 : 0, lowerCase);
        var (exitCode, observation) = await snapshot.ObserveAsync();

        Assert.Equal(0, exitCode);
        Assert.True(observation["readOnly"]!.GetValue<bool>());
        Assert.Equal((int)RuntimeModelLifecycle.TerminalPendingSettlement, observation["model"]!["lifecycle"]!.GetValue<int>());
        Assert.Equal(19, observation["sources"]!.AsArray().Count);
        Assert.Equal("bpmn:a29cc5c675038cc802eabef05ca3c36a517ddbf17c66304badea80379b1e56cd",
            observation["model"]!["executionId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Partial_standalone_source_projection_groups_are_refused(int sourceColumns)
    {
        using var snapshot = await Snapshot.CreateAsync(claimDelivery: true, sourceColumns);
        AssertRefused(await snapshot.ObserveAsync());
    }

    [Theory]
    [InlineData("SourceKind", false)]
    [InlineData("SourceKind", true)]
    [InlineData("ClaimDeliveredAtUtcTicks", false)]
    public async Task Present_projection_corruption_is_still_refused(string column, bool lowerCase)
    {
        using var snapshot = await Snapshot.CreateAsync(claimDelivery: true, sourceColumns: 4, lowerCase);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot.Path, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE elsa_runtime_post_commit_outbox SET {column}=1";
            await command.ExecuteNonQueryAsync();
        }
        AssertRefused(await snapshot.ObserveAsync());
    }

    private static void AssertRefused((int ExitCode, JsonNode Observation) result)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("ObservationUnavailable", result.Observation["status"]!.GetValue<string>());
        Assert.Equal("InvalidDataException", result.Observation["failureType"]!.GetValue<string>());
    }

    private sealed class Snapshot : IDisposable
    {
        private static readonly string[] SourceColumns = ["SourceKind", "SourceId", "TriggerIdHash", "SourceKeyHash"];
        private readonly DirectoryInfo _directory;

        private Snapshot(DirectoryInfo directory) => _directory = directory;

        internal string Path => System.IO.Path.Combine(_directory.FullName, "capture.sqlite");

        internal static async Task<Snapshot> CreateAsync(bool claimDelivery, int sourceColumns, bool lowerCase = false)
        {
            var snapshot = new Snapshot(Directory.CreateTempSubdirectory("elsa-readonly-outbox-shape-"));
            try
            {
                var fixture = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "delayed-compensation", "queued-result.sqlite.gz");
                await using (var input = File.OpenRead(fixture))
                await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                await using (var output = File.Create(snapshot.Path)) await gzip.CopyToAsync(output);
                Assert.Equal("dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63",
                    Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(snapshot.Path))));

                await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot.Path, Pooling = false }.ToString());
                await connection.OpenAsync();
                var columns = SourceColumns.Take(sourceColumns).ToList();
                if (claimDelivery) columns.Add("ClaimDeliveredAtUtcTicks");
                foreach (var column in columns)
                {
                    await using var command = connection.CreateCommand();
                    var type = column is "SourceKind" or "ClaimDeliveredAtUtcTicks" ? "INTEGER" : "TEXT";
                    var name = lowerCase ? column.ToLowerInvariant() : column;
                    command.CommandText = $"ALTER TABLE elsa_runtime_post_commit_outbox ADD COLUMN {name} {type}";
                    await command.ExecuteNonQueryAsync();
                }
                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        internal async Task<(int ExitCode, JsonNode Observation)> ObserveAsync()
        {
            var before = SHA256.HashData(await File.ReadAllBytesAsync(Path));
            var assembly = typeof(ReadOnlyRuntimeSnapshotTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BpmnHostingReaderAssembly").Value;
            Assert.NotNull(assembly);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { assembly, "--read-bpmn-retirement", Path,
                         "bpmn:a29cc5c675038cc802eabef05ca3c36a517ddbf17c66304badea80379b1e56cd" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The snapshot observer did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                throw;
            }
            Assert.True(string.IsNullOrWhiteSpace(await error), await error);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(Path)));
            return (process.ExitCode, JsonNode.Parse(await output)!);
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
