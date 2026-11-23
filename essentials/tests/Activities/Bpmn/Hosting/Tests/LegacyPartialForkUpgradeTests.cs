using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Recovers actual pre-writer databases, rather than constructing old-looking state.</summary>
public sealed class LegacyPartialForkUpgradeTests
{
    [Fact]
    public async Task Actual_legacy_partial_forks_upgrade_and_finish_through_production_commands()
    {
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_CAPTURE_DIRECTORY") ??
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-partial-forks");
        AssertClosedCapture(directory);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")))!;
        var originalHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "runtime.sqlite", "design.sqlite", "cases.json" })
        {
            var asset = Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == name)!;
            originalHashes[name] = Hash(await File.ReadAllBytesAsync(Path.Combine(directory, name)));
            Assert.Equal(asset["sha256"]!.GetValue<string>(), originalHashes[name]);
        }
        var cases = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "cases.json")))!.AsArray();
        Assert.Equal(8, cases.Count);
        var originalMigrations = await MigrationsAsync(Path.Combine(directory, "runtime.sqlite"), immutable: true);
        var originalArtifacts = await ArtifactsAsync(Path.Combine(directory, "runtime.sqlite"), immutable: true);
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: directory);
        var processId = host.ProcessId;
        await host.LoginAsync();
        var migrated = await MigrationsAsync(host.RuntimeDatabase);
        Assert.All(originalMigrations, migration => Assert.Contains(migration, migrated));
        Assert.Contains(migrated, migration => migration.StartsWith("20261028210825_ModelExecutionState|", StringComparison.Ordinal));
        Assert.Contains(migrated, migration => migration.StartsWith("20261028214345_ModelMaterialKind|", StringComparison.Ordinal));
        Assert.Equal(originalArtifacts, await ArtifactsAsync(host.RuntimeDatabase));
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var baseline = item!;
            var execution = baseline["workflowExecutionId"]!.GetValue<string>();
            var path = "/runtime/workflows/instances/" + execution;
            var recovered = await host.GetAsync(path);
            SamePin(baseline["heldView"]!["instance"]!, recovered["instance"]!);
            Assert.Equal("Running", recovered["instance"]!["status"]!.GetValue<string>());
            Assert.Equal(baseline["persistenceMode"]!.GetValue<string>(), recovered["persistenceMode"]!.GetValue<string>());
            foreach (var node in new[] { "wait-a", "wait-b" })
            {
                var prior = Activity(baseline["heldView"]!, node);
                var current = Activity(recovered, node);
                foreach (var field in new[] { "activityExecutionId", "executableNodeId", "status", "activityTypeVersion" })
                    Assert.Equal(prior[field]!.GetValue<string>(), current[field]!.GetValue<string>());
                Assert.True(JsonNode.DeepEquals(prior["outcomeNames"], current["outcomeNames"]));
            }
            var eventName = baseline["events"]!["b"]!.GetValue<string>();
            var delivery = await host.PostAsync("/runtime/workflows/stimuli", new
            {
                stimulusType = "Event", stimulusHash = "sha256:" + Hash(Encoding.UTF8.GetBytes(eventName)),
                mode = "ResumeOnly", correlationId = execution, input = new { eventName }
            });
            Assert.Equal(0, delivery["startedCount"]!.GetValue<int>());
            Assert.Equal(1, delivery["resumedCount"]!.GetValue<int>());
            var completed = await host.GetAsync(path);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (completed["instance"]!["status"]!.GetValue<string>() == "Running")
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The upgraded legacy process did not finish: " + execution);
                await Task.Delay(100);
                completed = await host.GetAsync(path);
            }
            Assert.Equal("Completed", completed["instance"]!["status"]!.GetValue<string>());
            Assert.Empty(completed["incidents"]!.AsArray());
            SamePin(recovered["instance"]!, completed["instance"]!);
            foreach (var node in new[] { "wait-a", "wait-b" })
            {
                var activity = Activity(completed, node);
                Assert.Equal(Activity(recovered, node)["activityExecutionId"]!.GetValue<string>(), activity["activityExecutionId"]!.GetValue<string>());
                Assert.Equal("Completed", activity["status"]!.GetValue<string>());
                Assert.Equal("Done", Assert.Single(activity["outcomeNames"]!.AsArray())!.GetValue<string>());
            }
            if (baseline["parent"]!.GetValue<string>() != "LegacyRoot")
                Assert.Equal("Completed", Activity(completed, "after-bpmn")["status"]!.GetValue<string>());
            observations.Add(new { parent = baseline["parent"]!.GetValue<string>(), persistenceMode = baseline["persistenceMode"]!.GetValue<string>(),
                executionId = execution, recovered, delivery, completed });
        }
        await host.StopAsync();
        Assert.Equal(originalArtifacts, await ArtifactsAsync(host.RuntimeDatabase));
        foreach (var (name, hash) in originalHashes)
            Assert.Equal(hash, Hash(await File.ReadAllBytesAsync(Path.Combine(directory, name))));
        AssertClosedCapture(directory);
        var snapshot = await host.SnapshotAsync("upgraded-runtime.sqlite.gz", "legacy-partial-forks");
        var designSnapshot = await host.SnapshotAsync("upgraded-design.sqlite.gz", "legacy-partial-forks", design: true);
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidenceRoot)
        {
            var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
            var evidence = new { formatVersion = 1, kind = "actual-legacy-partial-fork-upgrade", sourceRevision = source, capturedAt = DateTimeOffset.UtcNow,
                baselineSourceRevision = manifest["executingSourceCommit"]!.GetValue<string>(), baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json"))),
                baselineAssets = originalHashes, processId, profile = "production Workbench / EF SQLite / copied actual legacy Runtime and Design stores",
                originalMigrations, migrated, retainedArtifactCount = originalArtifacts.Count, retainedArtifacts = originalArtifacts, observations,
                binaries = host.DescribeBinaries(source), snapshot, designSnapshot,
                limits = new[] { "Partial-fork legacy root and Sequence/Flowchart/CompensationScope, Balanced/Durable only.",
                    "Pending callbacks, broadcast cohorts, MI/compensation/control and Studio need separate changed-host verification.",
                    "No native/ETL service binding, other-provider or clustered placement acceptance." } };
            var output = Path.Combine(evidenceRoot, "legacy-partial-forks"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(evidence,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    private static void AssertClosedCapture(string directory)
    {
        foreach (var name in new[] { "runtime.sqlite", "design.sqlite" })
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                Assert.False(File.Exists(Path.Combine(directory, name + suffix)), "The original capture must remain closed and immutable.");
    }
}
