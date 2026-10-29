using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyNativeParentUpgradeTests
{
    [Fact]
    public async Task Actual_pending_native_parent_faults_recover_with_their_original_composite_projection()
    {
        var manifestPath = Fixture("manifest.json", "legacy-baseline-manifest.json");
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var referencePath = Fixture("native-parents/api.json", "legacy-native-terminal-baseline.json");
        VerifyAsset(baseline, "native-parents/api.json", referencePath);
        var reference = JsonNode.Parse(await File.ReadAllTextAsync(referencePath))!;
        var observations = new List<object>();
        foreach (var mode in new[] { "Durable", "Balanced" })
        foreach (var parent in new[] { "Sequence", "Flowchart", "CompensationScope" })
        {
            var caseId = parent.ToLowerInvariant() + "-" + mode.ToLowerInvariant();
            var input = Fixture("native-parent-pending");
            foreach (var suffix in new[] { "-api.json", "-marker.json", ".sqlite.gz" })
                VerifyAsset(baseline, "native-parent-pending/" + caseId + suffix, Path.Combine(input, caseId + suffix));
            var captured = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, caseId + "-api.json")))!;
            var marker = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, caseId + "-marker.json")))!;
            var execution = captured["workflowExecutionId"]!.GetValue<string>();
            var sourceId = marker["sourceOutboxItemId"]!.GetValue<string>();
            Assert.True(captured["pendingParentConsumption"]!.GetValue<bool>());
            Assert.Equal(execution, marker["workflowExecutionId"]!.GetValue<string>());
            Assert.Equal("EF.TransactionCommitted", marker["providerEvent"]!.GetValue<string>());
            var expected = Assert.Single(reference["cases"]!.AsArray(), item => item!["parent"]!.GetValue<string>() == parent &&
                item["persistenceMode"]!.GetValue<string>() == mode && item["outcome"]!.GetValue<string>() == "Fault")!["after"]!;
            var temporary = Directory.CreateTempSubdirectory("elsa-legacy-parent-input-");
            try
            {
                var database = Path.Combine(temporary.FullName, "runtime.sqlite");
                await using (var source = File.OpenRead(Path.Combine(input, caseId + ".sqlite.gz")))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var target = File.Create(database)) await gzip.CopyToAsync(target);
                var rawAsset = Assert.Single(baseline["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "native-parent-pending/" + caseId + ".sqlite.gz")!;
                Assert.Equal(rawAsset["databaseSha256"]!.GetValue<string>(), Hash(await File.ReadAllBytesAsync(database)));
                var originalArtifacts = await ArtifactsAsync(database, immutable: true);
                var originalMigrations = await MigrationsAsync(database, immutable: true);
                var originalSource = await SourceAsync(database, sourceId, immutable: true);
                Assert.Equal(RuntimePostCommitOutboxStatus.Pending, originalSource.Status);
                Assert.True(JsonNode.DeepEquals(marker["work"], originalSource.Document["intent"]!["payload"]));
                await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeSnapshotPath: database,
                    recoverySweepSeconds: 1, legacyCombinedStore: true);
                AssertStandardHost(host);
                var processId = host.ProcessId;
                await host.LoginAsync();
                var path = "/runtime/workflows/instances/" + execution;
                var completed = await host.GetAsync(path);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
                while (completed["instance"]!["status"]!.GetValue<string>() != expected["instance"]!["status"]!.GetValue<string>() ||
                    (await SourceAsync(host.RuntimeDatabase, sourceId)).Status != RuntimePostCommitOutboxStatus.Delivered ||
                    await QueuedAsync(host.RuntimeDatabase, execution) != 0)
                {
                    Assert.True(DateTimeOffset.UtcNow < deadline, "The actual pending parent did not recover: " + caseId + " / " + completed.ToJsonString());
                    await Task.Delay(200);
                    completed = await host.GetAsync(path);
                }
                Assert.Equal(mode, completed["persistenceMode"]!.GetValue<string>());
                SamePin(captured["after"]!["instance"]!, completed["instance"]!);
                foreach (var node in new[] { "native-parent", "legacy-bpmn", "node-held" })
                {
                    Assert.Equal(Activity(captured["after"]!, node)["activityExecutionId"]!.GetValue<string>(), Activity(completed, node)["activityExecutionId"]!.GetValue<string>());
                    if (node == "node-held") continue;
                    Assert.Equal(Activity(expected, node)["status"]!.GetValue<string>(), Activity(completed, node)["status"]!.GetValue<string>());
                    Assert.True(JsonNode.DeepEquals(Activity(expected, node)["outcomeNames"], Activity(completed, node)["outcomeNames"]));
                }
                Assert.DoesNotContain(completed["activities"]!.AsArray(), activity => activity!["executableNodeId"]!.GetValue<string>() == "after-bpmn");
                var retainedIncidents = completed["incidents"]!.AsArray().Select(incident => incident!["incidentId"]!.GetValue<string>()).ToArray();
                Assert.All(captured["after"]!["incidents"]!.AsArray(), incident => Assert.Contains(incident!["incidentId"]!.GetValue<string>(), retainedIncidents));
                // Flowchart adds its native-parent incident; CompensationScope resolves the original child incident.
                // Compare the actual pre-writer terminal projection, rather than requiring an unprocessed incident set.
                Assert.Equal(IncidentProjection(expected), IncidentProjection(completed));
                var acceptedSource = await SourceAsync(host.RuntimeDatabase, sourceId);
                Assert.True(JsonNode.DeepEquals(originalSource.Document["intent"], acceptedSource.Document["intent"]));
                var migrations = await MigrationsAsync(host.RuntimeDatabase);
                Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations));
                await host.RestartAsync();
                var successorId = host.ProcessId;
                Assert.NotEqual(processId, successorId);
                await host.LoginAsync();
                var replayed = await host.GetAsync(path);
                Assert.True(JsonNode.DeepEquals(completed, replayed));
                var replayedSource = await SourceAsync(host.RuntimeDatabase, sourceId);
                Assert.Equal(acceptedSource.ContentSha256, replayedSource.ContentSha256);
                Assert.Equal(acceptedSource.Revision, replayedSource.Revision);
                Assert.Equal(0, await QueuedAsync(host.RuntimeDatabase, execution));
                await host.StopAsync();
                Assert.Equal(originalArtifacts, await ArtifactsAsync(host.RuntimeDatabase));
                var snapshot = await host.SnapshotAsync(caseId + "-runtime.sqlite.gz", "legacy-pending-parent");
                observations.Add(new { caseId, parent, persistenceMode = mode, executionId = execution,
                    baselineSourceRevision = captured["sourceRevision"]!.GetValue<string>(), sourceOutboxItemId = sourceId,
                    inputGzipSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(input, caseId + ".sqlite.gz"))),
                    originalProcessId = processId, restartedProcessId = successorId, originalMigrations, migrations,
                    retainedArtifacts = originalArtifacts, originalSource, acceptedSource, replayedSource,
                    captured = captured["after"]!.DeepClone(), completed, replayed, snapshot,
                    parentOutcomeApplied = parent != "Sequence",
                    callbackDisposition = parent == "Sequence" ? "Native terminal disposition; parent consumption is not claimed." : "Native parent outcome applied.",
                    binaries = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is not null
                        ? host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")) : null });
            }
            finally { temporary.Delete(recursive: true); }
        }
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
        {
            var output = Path.Combine(evidence, "legacy-pending-parent"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new
            {
                formatVersion = 1, kind = "actual-legacy-pending-parent-upgrade", capturedAt = DateTimeOffset.UtcNow,
                sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"),
                baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), referenceReceiptSha256 = Hash(await File.ReadAllBytesAsync(referencePath)),
                observations, testOnlyRecoveryProbePresent = false,
                limits = new[] { "Only pending native-parent fault notifications in Sequence/Flowchart/CompensationScope, Balanced/Durable.",
                    "Sequence retains its existing Faulted-workflow/Running-parent projection; an empty queue is not parent consumption.",
                    "Other legacy families, Studio, native/ETL bindings and clustered placement remain open." }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    [Fact]
    public async Task Actual_native_fault_and_cancel_terminals_preserve_their_projection_after_upgrade_and_restart()
    {
        var manifestPath = Fixture("manifest.json", "legacy-baseline-manifest.json");
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var referencePath = Fixture("native-parents/api.json", "legacy-native-terminal-baseline.json");
        var compressedPath = Fixture("native-parents/runtime.sqlite.gz", "legacy-native-terminal-baseline.sqlite.gz");
        var compressedDesignPath = Fixture("native-parents/design.sqlite.gz", "legacy-native-terminal-design.sqlite.gz");
        VerifyAsset(baseline, "native-parents/api.json", referencePath); VerifyAsset(baseline, "native-parents/runtime.sqlite.gz", compressedPath);
        VerifyAsset(baseline, "native-parents/design.sqlite.gz", compressedDesignPath);
        var reference = JsonNode.Parse(await File.ReadAllTextAsync(referencePath))!;
        Assert.Equal(12, reference["cases"]!.AsArray().Count);
        var temporary = Directory.CreateTempSubdirectory("elsa-legacy-terminals-input-");
        try
        {
            var database = Path.Combine(temporary.FullName, "runtime.sqlite");
            foreach (var (name, path) in new[] { ("runtime", compressedPath), ("design", compressedDesignPath) })
            {
                await using var source = File.OpenRead(path); await using var gzip = new GZipStream(source, CompressionMode.Decompress);
                await using var target = File.Create(Path.Combine(temporary.FullName, name + ".sqlite")); await gzip.CopyToAsync(target);
            }
            var originalMigrations = await MigrationsAsync(database, immutable: true);
            var artifacts = await ArtifactsAsync(database, immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: temporary.FullName, recoverySweepSeconds: 1);
            AssertStandardHost(host); await host.LoginAsync();
            var processId = host.ProcessId;
            var observed = new List<object>();
            var retained = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var item in reference["cases"]!.AsArray())
            {
                var execution = item!["workflowExecutionId"]!.GetValue<string>();
                var current = await host.GetAsync("/runtime/workflows/instances/" + execution);
                SamePin(item["after"]!["instance"]!, current["instance"]!);
                Assert.Equal(item["after"]!["instance"]!["status"]!.GetValue<string>(), current["instance"]!["status"]!.GetValue<string>());
                Assert.Equal(item["persistenceMode"]!.GetValue<string>(), current["persistenceMode"]!.GetValue<string>());
                foreach (var activity in item["after"]!["activities"]!.AsArray())
                {
                    var actual = Activity(current, activity!["executableNodeId"]!.GetValue<string>());
                    foreach (var field in new[] { "activityExecutionId", "status", "activityTypeVersion" })
                        Assert.Equal(activity[field]!.GetValue<string>(), actual[field]!.GetValue<string>());
                    Assert.True(JsonNode.DeepEquals(activity["outcomeNames"], actual["outcomeNames"]));
                }
                Assert.Equal(IncidentProjection(item["after"]!), IncidentProjection(current));
                Assert.DoesNotContain(current["activities"]!.AsArray(), activity => activity!["executableNodeId"]!.GetValue<string>() == "after-bpmn");
                retained.Add(execution, current);
                observed.Add(new { parent = item["parent"]!.GetValue<string>(), persistenceMode = item["persistenceMode"]!.GetValue<string>(),
                    outcome = item["outcome"]!.GetValue<string>(), executionId = execution, baseline = item["after"]!.DeepClone(), recovered = current });
            }
            var migrations = await MigrationsAsync(host.RuntimeDatabase);
            Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations));
            await host.RestartAsync(); var restartedProcessId = host.ProcessId; Assert.NotEqual(processId, restartedProcessId);
            await host.LoginAsync();
            foreach (var (execution, before) in retained)
                Assert.True(JsonNode.DeepEquals(before, await host.GetAsync("/runtime/workflows/instances/" + execution)));
            await host.StopAsync();
            Assert.Equal(artifacts, await ArtifactsAsync(host.RuntimeDatabase));
            var snapshot = await host.SnapshotAsync("native-terminals-runtime.sqlite.gz", "legacy-native-terminals");
            var designSnapshot = await host.SnapshotAsync("native-terminals-design.sqlite.gz", "legacy-native-terminals", design: true);
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var output = Path.Combine(evidence, "legacy-native-terminals"); Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new
                {
                    formatVersion = 1, kind = "actual-legacy-native-terminal-upgrade", capturedAt = DateTimeOffset.UtcNow,
                    sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), baselineSourceRevision = reference["sourceRevision"]!.GetValue<string>(),
                    baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), referenceReceiptSha256 = Hash(await File.ReadAllBytesAsync(referencePath)),
                    inputGzipSha256 = Hash(await File.ReadAllBytesAsync(compressedPath)), inputDesignGzipSha256 = Hash(await File.ReadAllBytesAsync(compressedDesignPath)),
                    originalMigrations, migrations, retainedArtifacts = artifacts,
                    originalProcessId = processId, restartedProcessId, observations = observed, snapshot, designSnapshot, testOnlyRecoveryProbePresent = false,
                    binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")),
                    limits = new[] { "Existing native Fault/Cancel terminal history only; no active token recovery is claimed here.",
                        "Composite-specific fault projections remain unchanged; this does not resolve #3242." }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
            }
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static string Fixture(string relative, string? copied = null) => Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_MATRIX_DIRECTORY") is { } root
        ? Path.Combine(root, relative) : Path.Combine(AppContext.BaseDirectory, "Fixtures", copied ?? relative);


    private static void VerifyAsset(JsonNode manifest, string name, string path) =>
        Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == name)!["sha256"]!.GetValue<string>(), Hash(File.ReadAllBytes(path)));

    private static string Key(string text) => Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
    private static string[] IncidentProjection(JsonNode view) => view["incidents"]!.AsArray().Select(incident =>
        string.Join('|', new[] { "executableNodeId", "severity", "status", "failureType", "isBlocking" }.Select(field => incident![field]!.ToJsonString())))
        .Order(StringComparer.Ordinal).ToArray();
    private sealed record SourceObservation(RuntimePostCommitOutboxStatus Status, long Revision, string ContentSha256, JsonNode Document);
    private static async Task<SourceObservation> SourceAsync(string database, string sourceId, bool immutable = false)
    {
        await using var connection = Read(database, immutable); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status, Revision, ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = $id";
        command.Parameters.AddWithValue("$id", Key(sourceId));
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync(), "The original committed callback is missing.");
        var json = reader.GetString(2);
        return new((RuntimePostCommitOutboxStatus)reader.GetInt32(0), reader.GetInt64(1), Hash(Encoding.UTF8.GetBytes(json)), JsonNode.Parse(json)!);
    }
    private static async Task<long> QueuedAsync(string database, string execution)
    {
        await using var connection = Read(database); await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item WHERE WorkflowExecutionId = $id";
        command.Parameters.AddWithValue("$id", Key(execution)); return (long)(await command.ExecuteScalarAsync())!;
    }
}
