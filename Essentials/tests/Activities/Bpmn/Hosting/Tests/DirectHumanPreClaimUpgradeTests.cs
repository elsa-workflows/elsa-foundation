using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Restores actual pre-claim writer stores into the current host before any claim writer exists. The original
/// completion command must still complete, replay and survive restart exactly as the old writer recorded it.
/// </summary>
public sealed class DirectHumanPreClaimUpgradeTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "pre-claim");

    public static TheoryData<string> Captures => new() { "preclaim", "human" };

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task Actual_pre_claim_wait_completes_with_its_original_command_on_the_current_host(string name)
    {
        var capture = await CaptureAsync(name);
        var input = await RestoreAsync(capture, capture.WaitStage);
        try
        {
            var artifacts = await ArtifactsAsync(Path.Combine(input.FullName, "runtime.sqlite"), immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: input.FullName,
                authoredDirectPublication: capture.Authored);
            AssertStandardHost(host);
            await host.LoginAsync();
            var path = "/bpmn/executions/" + Uri.EscapeDataString(capture.ExecutionId);
            SameView(capture.WaitView, await host.GetAsync(path));
            if (capture.WaitWork is not null) SameView(capture.WaitWork, (await host.GetAsync(path + "/work"))["items"]!);
            if (capture.WaitInspection is not null) SameView(capture.WaitInspection, await host.GetAsync(path + "/inspection"));
            var completion = path + "/work/" + Uri.EscapeDataString(capture.InvocationId) + "/complete";
            var obsolete = await host.PostAsync(completion, new { requestId = "t027-obsolete", attemptId = "obsolete-attempt" }, HttpStatusCode.Conflict);
            Assert.Equal("obsolete-human-work", obsolete["result"]!["code"]!.GetValue<string>());
            SameView(capture.WaitView, await host.GetAsync(path));

            SameView(capture.Accepted, await host.PostAsync(completion, capture.Command));
            SameView(capture.Replayed, await host.PostAsync(completion, capture.Command));
            var completed = await host.GetAsync(path);
            foreach (var field in new[] { "executionId", "executable", "protocol", "materialIncarnationId", "revision", "status", "result", "createdAt" })
                SameView(capture.TerminalView[field]!, completed[field]!);
            Assert.Empty((await host.GetAsync(path + "/work"))["items"]!.AsArray());

            var process = host.ProcessId;
            await host.RestartAsync();
            Assert.NotEqual(process, host.ProcessId);
            await host.LoginAsync();
            SameView(capture.Replayed, await host.PostAsync(completion, capture.Command));
            Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(path)));
            await host.StopAsync();
            await AssertDirectOnlyAsync(host.RuntimeDatabase, artifacts);
        }
        finally
        {
            input.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task Actual_pre_claim_completion_replays_its_original_result_on_the_current_host(string name)
    {
        var capture = await CaptureAsync(name);
        var input = await RestoreAsync(capture, capture.TerminalStage);
        try
        {
            var artifacts = await ArtifactsAsync(Path.Combine(input.FullName, "runtime.sqlite"), immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: input.FullName,
                authoredDirectPublication: capture.Authored);
            AssertStandardHost(host);
            await host.LoginAsync();
            var path = "/bpmn/executions/" + Uri.EscapeDataString(capture.ExecutionId);
            var completion = path + "/work/" + Uri.EscapeDataString(capture.InvocationId) + "/complete";
            SameView(capture.TerminalView, await host.GetAsync(path));
            if (capture.TerminalInspection is not null) SameView(capture.TerminalInspection, await host.GetAsync(path + "/inspection"));

            SameView(capture.Replayed, await host.PostAsync(completion, capture.Command));
            SameView(capture.Replayed, await host.GetAsync(path + "/commands/" + Uri.EscapeDataString(capture.Command["requestId"]!.GetValue<string>())));
            var late = await host.PostAsync(completion, new { requestId = "t027-late", attemptId = capture.Command["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
            Assert.Equal("execution-terminal", late["result"]!["code"]!.GetValue<string>());
            SameView(capture.TerminalView, await host.GetAsync(path));

            await host.RestartAsync();
            await host.LoginAsync();
            SameView(capture.Replayed, await host.PostAsync(completion, capture.Command));
            SameView(capture.TerminalView, await host.GetAsync(path));
            await host.StopAsync();
            await AssertDirectOnlyAsync(host.RuntimeDatabase, artifacts);
        }
        finally
        {
            input.Delete(recursive: true);
        }
    }

    private sealed record Capture(string Directory, bool Authored, string WaitStage, string TerminalStage, string ExecutionId, string InvocationId,
        JsonNode Command, JsonNode Accepted, JsonNode Replayed, JsonNode WaitView, JsonNode TerminalView,
        JsonNode? WaitWork, JsonNode? WaitInspection, JsonNode? TerminalInspection);

    private static async Task<Capture> CaptureAsync(string name)
    {
        await VerifyFixturesAsync();
        var directory = Path.Combine(Fixtures, name);
        if (name == "preclaim")
        {
            // The API collector's own records at its actual old writer (1d77ed131).
            var work = Assert.Single(await ReadAsync(directory, "work-before-restart.json") is JsonArray items ? items : [])!;
            var command = new JsonObject { ["requestId"] = "probe-complete", ["attemptId"] = work["attemptId"]!.GetValue<string>() };
            var accepted = await ReadAsync(directory, "completion.json");
            return new Capture(directory, false, "unclaimed-wait", "unclaimed-completed", accepted["executionId"]!.GetValue<string>(),
                work["invocationId"]!.GetValue<string>(), command, accepted, await ReadAsync(directory, "completion-replay.json"),
                await ReadAsync(directory, "execution-before-restart.json"), await ReadAsync(directory, "completed.json"),
                new JsonArray(work.DeepClone()), null, null);
        }

        // The actual Studio browser's completion request and both responses at its old writer (11437c0bd).
        var events = (await ReadAsync(directory, "browser-receipt.json"))["events"]!.AsArray()
            .Where(item => item!["method"]!.GetValue<string>() == "POST" && item["path"]!.GetValue<string>().EndsWith("/complete", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, events.Length);
        Assert.True(JsonNode.DeepEquals(events[0]!["request"], events[1]!["request"]));
        var segments = events[0]!["path"]!.GetValue<string>().Split('/');
        var executionId = Uri.UnescapeDataString(segments[3]);
        var waiting = await ReadAsync(directory, "human-wait-bpmn-views.json");
        var finished = await ReadAsync(directory, "finished-bpmn-views.json");
        return new Capture(directory, true, "human-wait", "finished", executionId, Uri.UnescapeDataString(segments[5]),
            events[0]!["request"]!.DeepClone(), events[0]!["response"]!.DeepClone(), events[1]!["response"]!.DeepClone(),
            Assert.Single(waiting["executions"]!["items"]!.AsArray())!.DeepClone(), Assert.Single(finished["executions"]!["items"]!.AsArray())!.DeepClone(),
            null, waiting["inspections"]![executionId]!.DeepClone(), finished["inspections"]![executionId]!.DeepClone());
    }

    private static async Task VerifyFixturesAsync()
    {
        var pins = await ReadAsync(Fixtures, "fixtures.json");
        foreach (var archive in pins["archives"]!.AsArray())
        foreach (var member in archive!["members"]!.AsArray())
        {
            var file = Path.Combine(Fixtures, member!["target"]!.GetValue<string>());
            Assert.True(File.Exists(file), "Restore the pinned actual captures first: python3 tools/bpmn/restore_evidence.py (" + file + ")");
            Assert.Equal(member["sha256"]!.GetValue<string>(), Hash(await File.ReadAllBytesAsync(file)));
        }
    }

    private static async Task<DirectoryInfo> RestoreAsync(Capture capture, string stage)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-preclaim-upgrade-");
        foreach (var store in new[] { "runtime", "design" })
        {
            await using var source = File.OpenRead(Path.Combine(capture.Directory, $"{stage}-{store}.sqlite.gz"));
            await using var gzip = new GZipStream(source, CompressionMode.Decompress);
            await using var target = File.Create(Path.Combine(directory.FullName, store + ".sqlite"));
            await gzip.CopyToAsync(target);
        }
        return directory;
    }

    private static async Task AssertDirectOnlyAsync(string database, SortedDictionary<string, string> artifacts)
    {
        Assert.Equal(artifacts, await ArtifactsAsync(database));
        await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        foreach (var table in new[] { "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    private static async Task<JsonNode> ReadAsync(string directory, string file) =>
        JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, file)))!;
}
