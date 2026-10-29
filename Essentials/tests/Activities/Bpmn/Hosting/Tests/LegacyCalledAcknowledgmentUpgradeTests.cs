using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyCalledAcknowledgmentUpgradeTests
{
    [Fact]
    public async Task Actual_accepted_called_results_recover_acknowledgment_without_repeating_the_caller()
    {
        var matrix = Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_MATRIX_DIRECTORY");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var manifestPath = matrix is null ? Path.Combine(fixtures, "legacy-baseline-manifest.json") : Path.Combine(matrix, "manifest.json");
        var input = Path.Combine(matrix ?? fixtures, "called-ack");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var observations = new List<object>();
        foreach (var cut in new[] { "notice", "terminal" })
        {
            foreach (var name in new[] { cut + "-held-api.json", cut + "-marker.json", cut + "-held.sqlite.gz" })
                Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "called-ack/" + name)!["sha256"]!.GetValue<string>(),
                    Hash(await File.ReadAllBytesAsync(Path.Combine(input, name))));
            var baseline = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, cut + "-held-api.json")))!;
            var original = Assert.Single(baseline["cases"]!.AsArray())!;
            var markerPath = Path.Combine(input, cut + "-marker.json");
            var marker = JsonNode.Parse(await File.ReadAllTextAsync(markerPath))!;
            var parentId = original["parentWorkflowExecutionId"]!.GetValue<string>();
            var childId = original["childWorkflowExecutionId"]!.GetValue<string>();
            var temporary = Directory.CreateTempSubdirectory("elsa-called-ack-upgrade-");
            try
            {
                var database = Path.Combine(temporary.FullName, "input.sqlite");
                await using (var source = File.OpenRead(Path.Combine(input, cut + "-held.sqlite.gz")))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var target = File.Create(database)) await gzip.CopyToAsync(target);
                var artifacts = await ArtifactsAsync(database, immutable: true);
                var originalMigrations = await MigrationsAsync(database, immutable: true);
                await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeSnapshotPath: database,
                    recoverySweepSeconds: 1, legacyCombinedStore: true);
                AssertStandardHost(host);
                var firstProcess = host.ProcessId;
                await host.LoginAsync();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
                while (await SourceStatusAsync(host.RuntimeDatabase, marker["sourceOutboxItemId"]!.GetValue<string>()) != 2)
                {
                    Assert.True(DateTimeOffset.UtcNow < deadline, "The original accepted source was not acknowledged: " + cut);
                    await Task.Delay(200);
                }
                var recoveredParent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
                var recoveredChild = await host.GetAsync("/runtime/workflows/instances/" + childId);
                SameView(original["parent"]!, recoveredParent);
                SameView(original["child"]!, recoveredChild);
                await host.StopAsync();
                var recoveredBackup = Path.Combine(temporary.FullName, "recovered.sqlite"); await BackupAsync(host.RuntimeDatabase, recoveredBackup);
                var recoveredApi = Receipt(original, recoveredParent, recoveredChild, cut == "notice" ? "NoticeAccepted" : "TerminalAccepted", "Observe");
                var recoveredApiPath = Path.Combine(temporary.FullName, "recovered-api.json"); await File.WriteAllTextAsync(recoveredApiPath, recoveredApi.ToJsonString());
                var recoveredProof = await InspectAsync(host.ReaderAssemblyPath, recoveredBackup, recoveredApiPath, markerPath, "recovered");
                var recoveredSnapshot = await ExportAsync(recoveredBackup, cut + "-recovered.sqlite.gz");
                await host.RestartAsync(); var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
                await host.LoginAsync();
                Assert.True(JsonNode.DeepEquals(recoveredParent, await host.GetAsync("/runtime/workflows/instances/" + parentId)));
                Assert.True(JsonNode.DeepEquals(recoveredChild, await host.GetAsync("/runtime/workflows/instances/" + childId)));
                JsonNode? childDelivery = null;
                if (cut == "notice")
                    childDelivery = await SendAsync(host, original["finishEventName"]!.GetValue<string>(), childId);
                var terminalParent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
                var terminalDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                while (Activity(terminalParent, "node-call")["status"]!.GetValue<string>() != "Completed")
                {
                    Assert.True(DateTimeOffset.UtcNow < terminalDeadline, "The original native call did not complete.");
                    await Task.Delay(100); terminalParent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
                }
                var warningDelivery = await SendAsync(host, original["warningEventName"]!.GetValue<string>(), parentId);
                var finishedParent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
                var finishedChild = await host.GetAsync("/runtime/workflows/instances/" + childId);
                var finishDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                while (finishedParent["instance"]!["status"]!.GetValue<string>() != "Completed" || finishedChild["instance"]!["status"]!.GetValue<string>() != "Completed")
                {
                    Assert.True(DateTimeOffset.UtcNow < finishDeadline, "The original caller/callee pair did not finish."); await Task.Delay(100);
                    finishedParent = await host.GetAsync("/runtime/workflows/instances/" + parentId); finishedChild = await host.GetAsync("/runtime/workflows/instances/" + childId);
                }
                Assert.Equal("Completed", finishedParent["instance"]!["status"]!.GetValue<string>());
                Assert.Equal("Completed", finishedChild["instance"]!["status"]!.GetValue<string>());
                Assert.Empty(finishedParent["incidents"]!.AsArray()); Assert.Empty(finishedChild["incidents"]!.AsArray());
                SamePin(original["parent"]!["instance"]!, finishedParent["instance"]!); SamePin(original["child"]!["instance"]!, finishedChild["instance"]!);
                Assert.Equal(Activity(recoveredParent, "node-warning")["activityExecutionId"]!.GetValue<string>(), Activity(finishedParent, "node-warning")["activityExecutionId"]!.GetValue<string>());
                Assert.Equal("Completed", Activity(finishedParent, "node-warning")["status"]!.GetValue<string>());
                Assert.Equal("Completed", Activity(finishedParent, "node-normal")["status"]!.GetValue<string>());
                await host.StopAsync();
                var finishedBackup = Path.Combine(temporary.FullName, "finished.sqlite"); await BackupAsync(host.RuntimeDatabase, finishedBackup);
                var finishedApi = Receipt(original, finishedParent, finishedChild, "Finished", "FinishWarning");
                var finishedApiPath = Path.Combine(temporary.FullName, "finished-api.json"); await File.WriteAllTextAsync(finishedApiPath, finishedApi.ToJsonString());
                var finishedProof = await InspectAsync(host.ReaderAssemblyPath, finishedBackup, finishedApiPath, markerPath, "finished");
                var finishedSnapshot = await ExportAsync(finishedBackup, cut + "-finished.sqlite.gz");
                var migrations = await MigrationsAsync(host.RuntimeDatabase); Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations));
                Assert.Equal(artifacts, await ArtifactsAsync(host.RuntimeDatabase));
                await host.RestartAsync(); var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess); await host.LoginAsync();
                Assert.True(JsonNode.DeepEquals(finishedParent, await host.GetAsync("/runtime/workflows/instances/" + parentId)));
                Assert.True(JsonNode.DeepEquals(finishedChild, await host.GetAsync("/runtime/workflows/instances/" + childId)));
                await host.StopAsync();
                observations.Add(new { cut, baselineSourceRevision = baseline["sourceRevision"]!.GetValue<string>(),
                    inputGzipSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + "-held.sqlite.gz"))),
                    baselineApiSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + "-held-api.json"))),
                    markerSha256 = Hash(await File.ReadAllBytesAsync(markerPath)),
                    marker, original, firstProcessId = firstProcess, secondProcessId = secondProcess, thirdProcessId = thirdProcess,
                    originalMigrations, migrations, retainedArtifacts = artifacts, recoveredApi, recoveredProof, recoveredSnapshot,
                    childDelivery, terminalParent, warningDelivery, finishedApi, finishedProof, finishedSnapshot,
                    binaries = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is not null
                        ? host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")) : null });
            }
            finally { temporary.Delete(recursive: true); }
        }
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
        {
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") is { } source)
                Assert.EndsWith("+" + source, FileVersionInfo.GetVersionInfo(ReaderAssembly()).ProductVersion);
            var output = Path.Combine(evidence, "legacy-called-ack"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new
            {
                formatVersion = 1, kind = "actual-legacy-called-ack-upgrade", capturedAt = DateTimeOffset.UtcNow,
                testOnlyRecoveryProbePresent = false,
                sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), observations,
                reader = new { file = Path.GetFileName(ReaderAssembly()), productVersion = FileVersionInfo.GetVersionInfo(ReaderAssembly()).ProductVersion,
                    sha256 = Hash(await File.ReadAllBytesAsync(ReaderAssembly())) },
                limits = new[] { "Legacy native/BpmnProcess called-control acknowledgment only; no direct-model parent bridge or settlement is proved.",
                    "Broadcast, active MI/compensation/control, actual Studio and native/ETL service integration remain open." }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    private static string ReaderAssembly() => typeof(LegacyCalledAcknowledgmentUpgradeTests).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "BpmnHostingReaderAssembly").Value!;
    private static void SameView(JsonNode expected, JsonNode actual)
    {
        var differences = new List<string>();
        Compare(expected, actual, "$", differences);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Take(20)));
        static void Compare(JsonNode? before, JsonNode? after, string path, List<string> differences)
        {
            if (JsonNode.DeepEquals(before, after)) return;
            // Rehydrated native timestamps may render UTC instead of the original offset; preserve the exact instant.
            var field = path[(path.LastIndexOf('.') + 1)..];
            if (field is "createdAt" or "startedAt" or "updatedAt" or "scheduledAt" or "lastCommittedAt" or "completedAt" &&
                before is JsonValue oldValue && after is JsonValue newValue && oldValue.TryGetValue<string>(out var oldText) &&
                newValue.TryGetValue<string>(out var newText) && DateTimeOffset.TryParse(oldText, out var oldTime) &&
                DateTimeOffset.TryParse(newText, out var newTime) && oldTime.UtcTicks == newTime.UtcTicks) return;
            if (before is JsonObject left && after is JsonObject right)
                foreach (var key in left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key))) Compare(left[key], right[key], path + "." + key, differences);
            else if (before is JsonArray first && after is JsonArray second && first.Count == second.Count)
                for (var i = 0; i < first.Count; i++) Compare(first[i], second[i], path + "[" + i + "]", differences);
            else differences.Add(path + ": " + before?.ToJsonString() + " -> " + after?.ToJsonString());
        }
    }
    private static JsonNode Receipt(JsonNode original, JsonNode parent, JsonNode child, string stage, string phase)
    {
        var item = original.DeepClone(); item["parent"] = parent.DeepClone(); item["child"] = child.DeepClone();
        return new JsonObject { ["schemaVersion"] = 1, ["passed"] = true, ["phase"] = phase, ["expectedStage"] = stage, ["cases"] = new JsonArray(item) };
    }
    private static async Task<JsonNode> SendAsync(OwnedWorkbench host, string name, string execution)
    {
        var result = await host.PostAsync("/runtime/workflows/stimuli", new { stimulusType = "Event", stimulusHash = "sha256:" + Hash(Encoding.UTF8.GetBytes(name)),
            mode = "ResumeOnly", correlationId = execution, input = new { eventName = name } });
        Assert.Equal(0, result["startedCount"]!.GetValue<int>()); Assert.Equal(1, result["resumedCount"]!.GetValue<int>()); return result;
    }
    private static async Task<long> SourceStatusAsync(string database, string id)
    {
        await using var connection = Read(database); await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = $id";
        command.Parameters.AddWithValue("$id", Convert.ToBase64String(Encoding.Unicode.GetBytes(id))); return (long)(await command.ExecuteScalarAsync())!;
    }
    private static async Task<JsonNode> InspectAsync(string reader, string database, string api, string marker, string phase)
    {
        var hash = Hash(await File.ReadAllBytesAsync(database));
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { reader, "--inspect-called-ack", database, hash, "default", api, marker, phase }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The checked snapshot reader did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(limit.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        var text = await output; Assert.True(process.ExitCode == 0, "Actual checked provider refused: " + text + await error);
        var result = JsonNode.Parse(text)!; Assert.True(result["providerChecked"]!.GetValue<bool>()); Assert.True(result["readOnly"]!.GetValue<bool>());
        Assert.Equal(hash, result["snapshotSha256"]!.GetValue<string>()); Assert.Equal(hash, Hash(await File.ReadAllBytesAsync(database))); return result;
    }
    private static async Task<object?> ExportAsync(string database, string name)
    {
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is not { } root) return null;
        var output = Path.Combine(root, "legacy-called-ack"); Directory.CreateDirectory(output); var path = Path.Combine(output, name);
        var data = await File.ReadAllBytesAsync(database);
        await using (var file = File.Create(path)) await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize)) await gzip.WriteAsync(data);
        return new { file = name, databaseSha256 = Hash(data), gzipSha256 = Hash(await File.ReadAllBytesAsync(path)) };
    }
}
