using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyBroadcastUpgradeTests
{
    [Fact]
    public async Task Actual_saved_broadcast_cuts_recover_their_exact_recipients_and_retire_normally()
    {
        var matrix = Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_MATRIX_DIRECTORY");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var manifestPath = matrix is null ? Path.Combine(fixtures, "legacy-baseline-manifest.json") : Path.Combine(matrix, "manifest.json");
        var input = Path.Combine(matrix ?? fixtures, "broadcast"); var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var observations = new List<object>();
        foreach (var cut in new[] { "before-selection", "after-cohort", "after-resume", "empty-cohort" })
        {
            foreach (var suffix in new[] { ".json", ".sqlite.gz" })
                Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "broadcast/" + cut + suffix)!["sha256"]!.GetValue<string>(),
                    Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + suffix))));
            var baseline = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, cut + ".json")))!;
            Assert.True(baseline["passed"]!.GetValue<bool>());
            var original = baseline["stopped"]!; var sourceId = baseline["cut"]!["sourceOutboxItemId"]!.GetValue<string>();
            var ids = original["workflows"]!.AsArray().Select(item => item!["workflowExecutionId"]!.GetValue<string>()).ToArray();
            var temporary = Directory.CreateTempSubdirectory("elsa-broadcast-upgrade-");
            try
            {
                var database = Path.Combine(temporary.FullName, "input.sqlite");
                await using (var source = File.OpenRead(Path.Combine(input, cut + ".sqlite.gz")))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var target = File.Create(database)) await gzip.CopyToAsync(target);
                Assert.Equal(original["snapshotSha256"]!.GetValue<string>(), Hash(await File.ReadAllBytesAsync(database)));
                var artifacts = await ArtifactsAsync(database, immutable: true); var originalMigrations = await MigrationsAsync(database, immutable: true);
                await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeSnapshotPath: database,
                    recoverySweepSeconds: 1, legacyCombinedStore: true);
                AssertStandardHost(host); var firstProcess = host.ProcessId; await host.LoginAsync();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
                while (await SourceStatusAsync(host.RuntimeDatabase, sourceId) != 2)
                {
                    Assert.True(DateTimeOffset.UtcNow < deadline, "The saved broadcast source was not acknowledged: " + cut); await Task.Delay(100);
                }
                var recoveredViews = await WaitForRecoveredViewsAsync(host, baseline["recovered"]!);
                await host.StopAsync(); var recoveredBackup = Path.Combine(temporary.FullName, "recovered.sqlite");
                await BackupAsync(host.RuntimeDatabase, recoveredBackup);
                var recoveredProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, recoveredBackup, null, new[] { sourceId }.Concat(ids).ToArray());
                AssertRecoveredFacts(cut, baseline, recoveredProof);
                var recoveredSnapshot = await ExportSnapshotAsync(recoveredBackup, cut + "-recovered.sqlite.gz", "legacy-broadcast");
                await host.RestartAsync(); var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess); await host.LoginAsync();
                foreach (var view in recoveredViews) Assert.True(JsonNode.DeepEquals(view.Value, await host.GetAsync("/runtime/workflows/instances/" + view.Key)));
                var secondBackup = Path.Combine(temporary.FullName, "second.sqlite"); await BackupAsync(host.RuntimeDatabase, secondBackup);
                var secondProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, secondBackup, null, new[] { sourceId }.Concat(ids).ToArray());
                Assert.True(JsonNode.DeepEquals(recoveredProof["source"], secondProof["source"]));
                Assert.True(JsonNode.DeepEquals(recoveredProof["workflows"], secondProof["workflows"]));

                // The after-resume fixture intentionally has no end: both catches loop forever. Retire those real native runs through CancelWorkflow.
                var loopingIds = cut == "after-resume" ? secondProof["workflows"]!.AsArray().Where(item =>
                    item!["bookmarks"]!.AsArray().Any(wait => wait!["executableNodeId"]!.GetValue<string>().StartsWith("node-wait", StringComparison.Ordinal)))
                    .Select(item => item!["workflowExecutionId"]!.GetValue<string>()).ToArray() : [];
                var cancelledIds = new HashSet<string>(loopingIds, StringComparer.Ordinal);
                var finishCommands = new List<JsonNode>(); var eventName = secondProof["source"]!["intentPayload"]!["eventName"]!.GetValue<string>();
                var hashes = secondProof["workflows"]!.AsArray().Where(item => !loopingIds.Contains(item!["workflowExecutionId"]!.GetValue<string>()))
                    .SelectMany(item => item!["bookmarks"]!.AsArray()).Select(wait => wait!["stimulusHash"]!.GetValue<string>()).Distinct().ToArray();
                foreach (var hash in hashes)
                {
                    var name = Assert.Single(new[] { eventName, "park-" + eventName, "unmatched-" + eventName }, candidate => "sha256:" + Hash(Encoding.UTF8.GetBytes(candidate)) == hash);
                    var delivery = await host.PostAsync("/runtime/workflows/stimuli", new { stimulusType = "Event", stimulusHash = hash, mode = "ResumeOnly", input = new { eventName = name } });
                    Assert.Equal(0, delivery["startedCount"]!.GetValue<int>());
                    if (delivery["resumedCount"]!.GetValue<int>() == 0)
                    {
                        // The existing generic HTTP route does not select among two exact waits. Preserve that native ambiguity contract.
                        Assert.NotEmpty(delivery["resumes"]!.AsArray());
                        foreach (var refusal in delivery["resumes"]!.AsArray())
                        {
                            Assert.Equal("Ambiguous", refusal!["status"]!.GetValue<string>());
                            var id = refusal["workflowExecutionId"]!.GetValue<string>();
                            var waiting = Assert.Single(secondProof["workflows"]!.AsArray(), item => item!["workflowExecutionId"]!.GetValue<string>() == id)!;
                            Assert.True(waiting["bookmarks"]!.AsArray().Count(wait => wait!["stimulusHash"]!.GetValue<string>() == hash) > 1);
                            cancelledIds.Add(id);
                        }
                    }
                    finishCommands.Add(delivery);
                }
                JsonNode? cancellation = null;
                if (cancelledIds.Count != 0)
                {
                    var accepted = await host.PostAsync("/runtime/workflows/alteration-plans", new { target = new { executionIds = cancelledIds.ToArray() },
                        alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } } }, HttpStatusCode.Accepted, "legacy-broadcast-cancel-" + Guid.NewGuid().ToString("N"));
                    var planId = accepted["planId"]!.GetValue<string>(); var cancellationDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                    do
                    {
                        cancellation = await host.GetAsync("/runtime/workflows/alteration-plans/" + planId);
                        if (cancellation["status"]!.GetValue<string>() == "Completed") break;
                        Assert.True(DateTimeOffset.UtcNow < cancellationDeadline, "Native cancellation did not finish: " + cancellation.ToJsonString()); await Task.Delay(100);
                    } while (true);
                }
                var finishedViews = new JsonObject();
                foreach (var view in recoveredViews)
                {
                    var finishDeadline = DateTimeOffset.UtcNow.AddSeconds(30); JsonNode finished;
                    do
                    {
                        finished = await host.GetAsync("/runtime/workflows/instances/" + view.Key);
                        if (finished["instance"]!["status"]!.GetValue<string>() == (cancelledIds.Contains(view.Key) ? "Cancelled" : "Completed")) break;
                        Assert.True(DateTimeOffset.UtcNow < finishDeadline, "The recovered broadcast recipient did not retire: " + view.Key); await Task.Delay(100);
                    } while (true);
                    SamePin(view.Value!["instance"]!, finished["instance"]!); Assert.Empty(finished["incidents"]!.AsArray()); finishedViews[view.Key] = finished;
                }
                await host.StopAsync(); var finishedBackup = Path.Combine(temporary.FullName, "finished.sqlite"); await BackupAsync(host.RuntimeDatabase, finishedBackup);
                var finishedProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, finishedBackup, null, new[] { sourceId }.Concat(ids).ToArray());
                Assert.True(JsonNode.DeepEquals(recoveredProof["source"], finishedProof["source"]));
                Assert.All(finishedProof["workflows"]!.AsArray(), workflow => Assert.Empty(workflow!["bookmarks"]!.AsArray()));
                Assert.All(finishedProof["allWorkflows"]!.AsArray(), workflow => Assert.Contains(workflow!["status"]!.GetValue<string>(), new[] { "Completed", "Cancelled" }));
                var finishedSnapshot = await ExportSnapshotAsync(finishedBackup, cut + "-finished.sqlite.gz", "legacy-broadcast");
                var migrations = await MigrationsAsync(host.RuntimeDatabase); Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations));
                Assert.Equal(artifacts, await ArtifactsAsync(host.RuntimeDatabase));
                await host.RestartAsync(); var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess); await host.LoginAsync();
                foreach (var view in finishedViews) Assert.True(JsonNode.DeepEquals(view.Value, await host.GetAsync("/runtime/workflows/instances/" + view.Key)));
                await host.StopAsync();
                observations.Add(new { cut, baselineSourceRevision = baseline["sourceRevision"]!.GetValue<string>(),
                    baselineApiSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + ".json"))), inputGzipSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + ".sqlite.gz"))),
                    originalStopped = original, referenceSource = baseline["recovered"]!["source"], firstProcessId = firstProcess, secondProcessId = secondProcess, thirdProcessId = thirdProcess,
                    originalMigrations, migrations, retainedArtifacts = artifacts, recoveredViews, recoveredProof, recoveredSnapshot, secondProof,
                    finishCommands, loopingIds, cancelledIds, cancellation, finishedViews, finishedProof, finishedSnapshot,
                    binaries = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is not null ? host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")) : null });
            }
            finally { temporary.Delete(recursive: true); }
            foreach (var suffix in new[] { ".json", ".sqlite.gz" })
                Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "broadcast/" + cut + suffix)!["sha256"]!.GetValue<string>(), Hash(await File.ReadAllBytesAsync(Path.Combine(input, cut + suffix))));
        }
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
        {
            var reader = typeof(LegacyBroadcastUpgradeTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "BpmnHostingReaderAssembly").Value!;
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") is { } source) Assert.EndsWith("+" + source, FileVersionInfo.GetVersionInfo(reader).ProductVersion);
            var output = Path.Combine(evidence, "legacy-broadcast"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new {
                formatVersion = 1, kind = "actual-legacy-broadcast-upgrade", capturedAt = DateTimeOffset.UtcNow,
                sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), testOnlyRecoveryProbePresent = false, observations,
                reader = new { file = Path.GetFileName(reader), productVersion = FileVersionInfo.GetVersionInfo(reader).ProductVersion, sha256 = Hash(await File.ReadAllBytesAsync(reader)) },
                limits = new[] { "Legacy native/BpmnProcess broadcast cuts only; no direct-model recipient dispatch or model settlement is proved.", "Original infinite loops and excluded listeners with an explicit native Ambiguous generic-HTTP result retire through existing CancelWorkflow; no new generic HTTP broadcast operation is claimed." }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    private static async Task<JsonObject> WaitForRecoveredViewsAsync(OwnedWorkbench host, JsonNode reference)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); var views = new JsonObject();
        foreach (var expected in reference["workflows"]!.AsArray())
        {
            Assert.True(expected!["exists"]!.GetValue<bool>()); var id = expected["workflowExecutionId"]!.GetValue<string>(); JsonNode view;
            do
            {
                view = await host.GetAsync("/runtime/workflows/instances/" + id);
                if (view["instance"]!["status"]!.GetValue<string>() == expected["status"]!.GetValue<string>() &&
                    view["activities"]!.AsArray().Count(activity => activity!["status"]!.GetValue<string>() == "Suspended") == expected["bookmarks"]!.AsArray().Count) break;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    var processId = host.ProcessId;
                    await host.StopAsync();
                    var snapshot = await host.SnapshotAsync("failed-recipient-" + id + ".sqlite.gz", "legacy-broadcast");
                    if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
                    {
                        var directory = Path.Combine(evidence, "legacy-broadcast"); Directory.CreateDirectory(directory);
                        await File.WriteAllTextAsync(Path.Combine(directory, "failed-recipient-" + id + ".json"), JsonSerializer.Serialize(new
                        {
                            processId, sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"),
                            productVersion = FileVersionInfo.GetVersionInfo(host.AssemblyPath).ProductVersion,
                            expected, observed = view, snapshot
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                    }
                    var states = string.Join(", ", view["activities"]!.AsArray().Select(activity =>
                        activity!["activityExecutionId"]!.GetValue<string>() + ":" + activity["status"]!.GetValue<string>()));
                    Assert.Fail("A saved broadcast recipient did not reach its expected boundary: " + id +
                        "; expected " + expected["status"] + ", observed " + view["instance"]!["status"] + "; activities " + states);
                }
                await Task.Delay(100);
            } while (true);
            foreach (var field in new[] { "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId" }) Assert.Equal(expected["pinnedExecutable"]![field]!.GetValue<string>(), view["instance"]![field]!.GetValue<string>());
            Assert.Empty(view["incidents"]!.AsArray()); views[id] = view;
        }
        return views;
    }

    private static void AssertRecoveredFacts(string cut, JsonNode baseline, JsonNode proof)
    {
        var original = baseline["stopped"]!; var source = proof["source"]!; var before = original["source"]!; var expected = baseline["recovered"]!["source"]!;
        Assert.Equal("Delivered", source["status"]!.GetValue<string>()); Assert.True(source["deliveryFencingToken"]!.GetValue<long>() > before["deliveryFencingToken"]!.GetValue<long>());
        foreach (var field in new[] { "outboxItemId", "intentFingerprint", "workflowExecutionId" }) Assert.Equal(before[field]!.GetValue<string>(), source[field]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["intentPayload"], source["intentPayload"])); Assert.NotNull(source["snapshot"]);
        Assert.True(JsonNode.DeepEquals(expected["selectedStarts"], source["selectedStarts"])); Assert.True(JsonNode.DeepEquals(expected["selectedResumes"], source["selectedResumes"]));
        if (cut == "before-selection")
        {
            Assert.Null(before["snapshot"]);
            Assert.True(JsonNode.DeepEquals(expected["snapshot"]!["starts"], source["snapshot"]!["starts"]));
            Assert.True(JsonNode.DeepEquals(expected["snapshot"]!["resumes"], source["snapshot"]!["resumes"]));
        }
        else Assert.True(JsonNode.DeepEquals(before["snapshot"], source["snapshot"]));
        foreach (var recipient in source["selectedResumes"]!.AsArray())
        {
            var workflow = Assert.Single(proof["workflows"]!.AsArray(), item => item!["workflowExecutionId"]!.GetValue<string>() == recipient!["workflowExecutionId"]!.GetValue<string>())!;
            Assert.DoesNotContain(workflow["bookmarks"]!.AsArray(), wait => wait!["bookmarkId"]!.GetValue<string>() == recipient!["bookmarkId"]!.GetValue<string>());
            Assert.Equal("Completed", Assert.Single(workflow["selectedActivities"]!.AsArray(), activity => activity!["activityExecutionId"]!.GetValue<string>() == recipient!["activityExecutionId"]!.GetValue<string>())!["status"]!.GetValue<string>());
        }
        foreach (var workflow in original["workflows"]!.AsArray().Where(item => item!["exists"]!.GetValue<bool>()))
        {
            var id = workflow!["workflowExecutionId"]!.GetValue<string>(); var current = Assert.Single(proof["workflows"]!.AsArray(), item => item!["workflowExecutionId"]!.GetValue<string>() == id)!;
            Assert.True(JsonNode.DeepEquals(workflow["pinnedExecutable"], current["pinnedExecutable"]));
            if (!source["selectedResumes"]!.AsArray().Any(item => item!["workflowExecutionId"]!.GetValue<string>() == id)) Assert.True(JsonNode.DeepEquals(workflow["bookmarks"], current["bookmarks"]));
        }
        if (cut == "after-resume")
        {
            var accepted = baseline["acceptedRecipient"]!; var rearmed = baseline["rearmedWait"]!;
            var old = Assert.Single(original["workflows"]!.AsArray(), workflow => workflow!["workflowExecutionId"]!.GetValue<string>() == accepted["workflowExecutionId"]!.GetValue<string>())!;
            var current = Assert.Single(proof["workflows"]!.AsArray(), workflow => workflow!["workflowExecutionId"]!.GetValue<string>() == accepted["workflowExecutionId"]!.GetValue<string>())!;
            Assert.True(JsonNode.DeepEquals(rearmed, Assert.Single(current["bookmarks"]!.AsArray(), wait => wait!["bookmarkId"]!.GetValue<string>() == rearmed["bookmarkId"]!.GetValue<string>())));
            foreach (var recipient in source["selectedResumes"]!.AsArray())
            {
                var prior = Assert.Single(old["selectedActivities"]!.AsArray(), activity => activity!["activityExecutionId"]!.GetValue<string>() == recipient!["activityExecutionId"]!.GetValue<string>())!;
                var after = Assert.Single(current["selectedActivities"]!.AsArray(), activity => activity!["activityExecutionId"]!.GetValue<string>() == recipient!["activityExecutionId"]!.GetValue<string>())!;
                if (recipient!["bookmarkId"]!.GetValue<string>() == accepted["bookmarkId"]!.GetValue<string>()) Assert.True(JsonNode.DeepEquals(prior, after));
                else Assert.Equal(prior["attemptCount"]!.GetValue<int>() + 1, after["attemptCount"]!.GetValue<int>());
            }
        }
        foreach (var start in source["selectedStarts"]!.AsArray())
        {
            var workflow = Assert.Single(proof["workflows"]!.AsArray(), item => item!["workflowExecutionId"]!.GetValue<string>() == start!["workflowExecutionId"]!.GetValue<string>())!;
            Assert.Equal("node-park", Assert.Single(workflow["bookmarks"]!.AsArray())!["executableNodeId"]!.GetValue<string>());
            Assert.Single(proof["allWorkflows"]!.AsArray(), item => item!["artifactId"]!.GetValue<string>() == start!["artifactId"]!.GetValue<string>());
        }
    }
}
