using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RestartFixture;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeHistoryProcessRestartTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Actual_runtime_capture_frozen_reads_resume_replay_and_purge_survive_distinct_processes(PersistenceMode mode)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-history-restart-");
        var nonce = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "owner.txt"), nonce);
        try
        {
            var seed = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "seed"));
            var first = seed.After;
            Assert.Equal("Running", first.WorkflowStatus); Assert.Equal("Suspended", first.ActivityStatus); Assert.Equal(1, first.BookmarkCount);
            Assert.Equal("Available", first.Availability); Assert.Equal(1, first.Total); Assert.Equal(0, first.Position);
            Assert.NotNull(first.SequenceToken); Assert.NotNull(first.ActivityId); Assert.NotNull(first.FirstEventId);
            Assert.NotNull(first.FirstRecordedAt); Assert.NotNull(first.FirstEvidenceHash); Assert.True(first.LastSequence > 0);
            Assert.Equal(first.LastSequence, first.Upper); Assert.True(first.NativeMarkers > 0); Assert.NotEmpty(first.Groups);
            AssertCadence(first);
            var resumed = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "resume", first.SequenceToken));
            Assert.NotNull(resumed.Before); AssertOriginal(first, resumed.Before);
            Assert.Equal(first.LastSequence, resumed.Before.LastSequence); Assert.Equal(first.NativeMarkers, resumed.Before.NativeMarkers);
            Assert.Equal(first.NativeEvidenceHash, resumed.Before.NativeEvidenceHash);
            AssertOriginal(first, resumed.After);
            Assert.Equal("Completed", resumed.After.WorkflowStatus); Assert.Equal("Completed", resumed.After.ActivityStatus);
            Assert.Equal(0, resumed.After.BookmarkCount); Assert.True(resumed.After.LastSequence > first.LastSequence);
            Assert.True(resumed.After.NativeMarkers > first.NativeMarkers);
            AssertCadence(resumed.After);
            Assert.All(first.Groups, group => Assert.Contains(group, resumed.After.Groups));

            var purged = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "purge", first.SequenceToken));
            Assert.NotNull(purged.Before); Assert.NotNull(purged.Replayed);
            AssertOriginal(first, purged.Before); AssertOriginal(first, purged.Replayed);
            Assert.Equal(resumed.After.NativeEvidenceHash, purged.Before.NativeEvidenceHash);
            Assert.Equal(purged.Before.NativeMarkers, purged.Replayed.NativeMarkers);
            Assert.Equal(purged.Before.NativeEvidenceHash, purged.Replayed.NativeEvidenceHash);
            Assert.Equal("Completed", purged.After.WorkflowStatus); Assert.Equal("Invalidated", purged.After.Availability);
            Assert.Equal(purged.Replayed.LastSequence, purged.After.PurgedThrough);
            Assert.Equal(purged.Replayed.NativeMarkers, purged.After.NativeMarkers); Assert.Null(purged.After.FirstEventId);

            var confirmed = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "confirm", first.SequenceToken));
            Assert.Equal("Completed", confirmed.After.WorkflowStatus); Assert.Equal(0, confirmed.After.BookmarkCount);
            Assert.Equal("Invalidated", confirmed.After.Availability); Assert.Null(confirmed.After.FirstEventId);
            Assert.Equal(purged.After.LastSequence, confirmed.After.LastSequence);
            Assert.Equal(purged.After.PurgedThrough, confirmed.After.PurgedThrough);
            Assert.Equal(purged.After.NativeMarkers, confirmed.After.NativeMarkers);
            var replies = new[] { seed, resumed, purged, confirmed };
            Assert.Equal(4, replies.Select(reply => (reply.ProcessId, reply.ProcessStartTicks)).Distinct().Count());
            Assert.All(replies, reply =>
            {
                Assert.Equal(mode.ToString(), reply.Mode); Assert.Equal(seed.ArtifactHash, reply.ArtifactHash);
                Assert.Equal(seed.Version, reply.Version); Assert.Equal(seed.AppliedMigrations, reply.AppliedMigrations);
                Assert.Equal(new[] { "20261017151421_Initial", "20261028210825_ModelExecutionState",
                    "20261028214345_ModelMaterialKind", "20261102222549_UserTasks",
                    "20261103045250_OutboxClaimDeliveredAt", "20261113205347_StandaloneTriggers", "20261114005143_RunHistoryTimeline", "20261118120000_VariableLifecycleInspection", "20261121222332_TimerHistoryOwnership" }, reply.AppliedMigrations);
            });
            output.WriteLine(JsonSerializer.Serialize(new { mode, replies }, Json));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertCadence(HistorySnapshot snapshot)
    {
        var physical = snapshot.Groups.Select(group => group.Physical).Distinct().Count();
        Assert.InRange((long)physical, 1, snapshot.NativeMarkers);
        Assert.True(snapshot.Groups.Length > physical, "Logical checkpoints must retain evidence inside actual grouped physical commits.");
        Assert.All(snapshot.Groups.GroupBy(group => group.Logical), group => Assert.Single(group.Select(item => item.Physical).Distinct()));
    }

    private static void AssertOriginal(HistorySnapshot original, HistorySnapshot retained)
    {
        Assert.Equal("Available", retained.Availability);
        Assert.Equal(original.Upper, retained.Upper); Assert.Equal(original.Total, retained.Total);
        Assert.Equal(original.Position, retained.Position); Assert.Equal(original.ActivityId, retained.ActivityId);
        Assert.Equal(original.FirstEventId, retained.FirstEventId); Assert.Equal(original.FirstSequence, retained.FirstSequence);
        Assert.Equal(original.FirstPhysical, retained.FirstPhysical); Assert.Equal(original.FirstRecordedAt, retained.FirstRecordedAt);
        Assert.Equal(original.FirstEvidenceHash, retained.FirstEvidenceHash);
    }

    private static async Task<RestartReply> Run(string directory, RestartRequest request)
    {
        var configuration = typeof(RuntimeHistoryProcessRestartTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Elsa.Server.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var assembly = Path.Combine(root.FullName, "essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Fixtures/RuntimeHistoryRestartHost/bin",
            configuration, "net10.0", "RuntimeHistoryRestartHost.dll");
        Assert.True(File.Exists(assembly), "The child fixture must be built through the test project reference.");
        var expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly)));
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add(assembly);
        var allowed = new[] { "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL" };
        var inherited = allowed.Where(key => Environment.GetEnvironmentVariable(key) is not null)
            .ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key)!);
        info.Environment.Clear(); foreach (var pair in inherited) info.Environment[pair.Key] = pair.Value;
        var home = Path.Combine(directory, "home"); Directory.CreateDirectory(home);
        info.Environment["HOME"] = home; info.Environment["DOTNET_CLI_HOME"] = home;
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = new Process { StartInfo = info };
        Assert.True(process.Start());
        var stdout = ReadBounded(process.StandardOutput, 65_536); var stderr = ReadBounded(process.StandardError, 32_768);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json)); process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var raw = await stdout; var diagnostics = await stderr;
            Assert.True(process.ExitCode == 0, $"Child exited {process.ExitCode}: {raw}\n{diagnostics}");
            var reply = JsonSerializer.Deserialize<RestartReply>(raw, Json); Assert.NotNull(reply);
            Assert.Equal("completed", reply.Status); Assert.Equal(process.Id, reply.ProcessId);
            Assert.Equal(expectedHash, reply.ArtifactHash); Assert.True(reply.ProcessStartTicks > 0);
            return reply;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync();
            // Readers were created here and must settle before this process and its owned directory retire.
            try { await stdout; } catch { }
            try { await stderr; } catch { }
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, int limit)
    {
        var result = new StringBuilder(); var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Owned fixture output exceeded its bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
