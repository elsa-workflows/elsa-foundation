using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.TimerRestartFixture;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class TimerHistoryProcessRestartTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Actual_published_delay_source_and_late_observations_survive_distinct_processes(PersistenceMode mode)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-timer-history-restart-"); var nonce = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "owner.txt"), nonce);
        try
        {
            var seed = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "seed"));
            output.WriteLine(JsonSerializer.Serialize(new { phase = seed }, Json));
            Assert.Equal("Running", seed.After.WorkflowStatus); Assert.Equal("Suspended", seed.After.ActivityStatus);
            Assert.Equal(1, seed.After.BookmarkCount); Assert.True(seed.After.ActiveTimer); Assert.True(seed.After.Pending);
            Assert.NotNull(seed.After.ScheduledId); Assert.NotNull(seed.After.ScheduledAt); Assert.Null(seed.After.ScheduleRecordedAt);
            Assert.Null(seed.After.DispatchedId); Assert.DoesNotContain("TimerDispatched", seed.After.EventNames);
            Assert.True(seed.After.CapturedFromStart); Assert.False(seed.After.HasGaps);

            var fired = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "fire"));
            output.WriteLine(JsonSerializer.Serialize(new { phase = fired }, Json));
            Assert.NotNull(fired.Before);
            Assert.Equal(seed.After, fired.Before with { EventNames = seed.After.EventNames, EventIds = seed.After.EventIds });
            Assert.Equal(seed.After.EventNames, fired.Before.EventNames); Assert.Equal(seed.After.EventIds, fired.Before.EventIds);
            Assert.Equal("Completed", fired.After.WorkflowStatus); Assert.Equal("Completed", fired.After.ActivityStatus);
            Assert.Equal(seed.After.ActivityId, fired.After.ActivityId); Assert.Equal(0, fired.After.BookmarkCount);
            Assert.False(fired.After.ActiveTimer); Assert.False(fired.After.Pending); Assert.False(fired.After.HasGaps);
            Assert.Equal(seed.After.ScheduledId, fired.After.ScheduledId); Assert.Equal(seed.After.ScheduledAt, fired.After.ScheduledAt);
            Assert.NotNull(fired.After.ScheduleRecordedAt); Assert.NotNull(fired.After.DispatchedId);
            Assert.Equal(seed.After.ScheduledId, fired.After.DispatchCause); Assert.True(fired.After.LastSequence > seed.After.LastSequence);
            foreach (var name in new[] { "TimerScheduled", "TimerDispatched", "BookmarkConsumed", "ActivityResumeClaimed", "ActivityCompleted" })
                Assert.Equal(1, fired.After.EventNames.Count(value => value == name));

            var confirmed = await Run(directory.FullName, new(directory.FullName, nonce, mode.ToString(), "confirm"));
            output.WriteLine(JsonSerializer.Serialize(new { phase = confirmed }, Json));
            Assert.NotNull(confirmed.Before);
            Assert.Equal(fired.After, confirmed.After with { EventNames = fired.After.EventNames, EventIds = fired.After.EventIds });
            Assert.Equal(fired.After.EventNames, confirmed.After.EventNames); Assert.Equal(fired.After.EventIds, confirmed.After.EventIds);
            Assert.Equal(fired.After.EventIds, confirmed.Before.EventIds);
            var replies = new[] { seed, fired, confirmed };
            Assert.Equal(3, replies.Select(reply => (reply.ProcessId, reply.ProcessStartTicks)).Distinct().Count());
            Assert.All(replies, reply =>
            {
                Assert.Equal(mode.ToString(), reply.Mode); Assert.Equal(seed.ArtifactHash, reply.ArtifactHash); Assert.Equal(seed.Version, reply.Version);
                Assert.Equal(RuntimeOperationalStateEfModule.TimerHistorySchemaVersion.ToString(), reply.TimerWriteVersion);
                Assert.Equal(new[] { "20261017151421_Initial", "20261028210825_ModelExecutionState", "20261028214345_ModelMaterialKind",
                    "20261102222549_UserTasks", "20261103045250_OutboxClaimDeliveredAt", "20261113205347_StandaloneTriggers",
                    "20261114005143_RunHistoryTimeline", "20261121222332_TimerHistoryOwnership" }, reply.AppliedMigrations);
            });
            output.WriteLine(JsonSerializer.Serialize(new { mode, replies }, Json));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<TimerRestartReply> Run(string directory, TimerRestartRequest request)
    {
        var configuration = typeof(TimerHistoryProcessRestartTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Elsa.Server.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var assembly = Path.Combine(root.FullName, "essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Fixtures/TimerHistoryRestartHost/bin",
            configuration, "net10.0", "TimerHistoryRestartHost.dll");
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
            var reply = JsonSerializer.Deserialize<TimerRestartReply>(raw, Json); Assert.NotNull(reply);
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
