using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class TemplateDesignUpgradeTests
{
    [Fact]
    public async Task Actual_cumulative_template_writer_store_retains_template_and_recovers_published_wait()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "templates-current-writer");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "manifest.json")))!;
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "case.json")))!;
        Assert.Equal("1ae57d214d31ad0d58526df557e4e4f13a4ef5cf", baseline["sourceRevision"]!.GetValue<string>());
        var input = Directory.CreateTempSubdirectory("elsa-actual-template-writer-").FullName;
        try
        {
            foreach (var name in new[] { "runtime", "design" })
            {
                var compressed = Path.Combine(fixtures, name + ".sqlite.gz");
                var asset = Assert.Single(manifest["assets"]!.AsArray(), item => item!["path"]!.GetValue<string>() == "current-writer/" + name + ".sqlite.gz")!;
                Assert.Equal(asset["sha256"]!.GetValue<string>(), Hash(await File.ReadAllBytesAsync(compressed)));
                await using var source = new GZipStream(File.OpenRead(compressed), CompressionMode.Decompress);
                await using var target = File.Create(Path.Combine(input, name + ".sqlite"));
                await source.CopyToAsync(target);
            }
            var originalHistory = await DesignHistoryAsync(Path.Combine(input, "design.sqlite"));
            Assert.Single(originalHistory);
            Assert.StartsWith("20261030020434_Initial|", originalHistory[0]);
            var originalArtifacts = await ArtifactsAsync(Path.Combine(input, "runtime.sqlite"), immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: input);
            AssertStandardHost(host);
            var processes = new List<int> { host.ProcessId };
            await host.LoginAsync();
            var execution = baseline["executionId"]!.GetValue<string>();
            var templateRoute = "/design/workflows/templates/" + baseline["templateId"]!.GetValue<string>();
            var recoveredTemplate = await host.GetAsync(templateRoute);
            SameView(baseline["template"]!, recoveredTemplate);
            var recovered = await host.GetAsync("/runtime/workflows/instances/" + execution);
            SameView(baseline["heldView"]!, recovered);
            Assert.Equal(originalArtifacts, await ArtifactsAsync(host.RuntimeDatabase));
            var created = await host.PostAsync("/design/workflows/templates", new
            {
                name = "Template saved after retained-baseline upgrade", operationKey = "upgrade-new-template",
                content = recoveredTemplate["content"]!.DeepClone()
            });
            var createdRoute = "/design/workflows/templates/" + created["id"]!.GetValue<string>();
            await host.RestartAsync();
            processes.Add(host.ProcessId);
            await host.LoginAsync();
            SameView(recoveredTemplate, await host.GetAsync(templateRoute));
            SameView(created, await host.GetAsync(createdRoute));
            SameView(recovered, await host.GetAsync("/runtime/workflows/instances/" + execution));
            var eventName = baseline["eventName"]!.GetValue<string>();
            var delivery = await host.PostAsync("/runtime/workflows/stimuli", new
            {
                stimulusType = "Event", stimulusHash = "sha256:" + Hash(Encoding.UTF8.GetBytes(eventName)),
                mode = "ResumeOnly", correlationId = execution, input = new { eventName }
            });
            Assert.Equal(0, delivery["startedCount"]!.GetValue<int>());
            Assert.Equal(1, delivery["resumedCount"]!.GetValue<int>());
            var completed = await host.GetAsync("/runtime/workflows/instances/" + execution);
            Assert.Equal("Completed", completed["instance"]!["status"]!.GetValue<string>());
            Assert.Empty(completed["incidents"]!.AsArray());
            SamePin(recovered["instance"]!, completed["instance"]!);
            Assert.Equal(Activity(recovered, "real-wait")["activityExecutionId"]!.GetValue<string>(), Activity(completed, "real-wait")["activityExecutionId"]!.GetValue<string>());
            await host.RestartAsync();
            processes.Add(host.ProcessId);
            await host.LoginAsync();
            SameView(completed, await host.GetAsync("/runtime/workflows/instances/" + execution));
            SameView(recoveredTemplate, await host.GetAsync(templateRoute));
            SameView(created, await host.GetAsync(createdRoute));
            await host.StopAsync();
            // The original cumulative template row stays untouched; the library delta appends its own row.
            Assert.Equal(originalHistory.Append("20261031002424_Initial|10.0.10"), await DesignHistoryAsync(host.DesignDatabase));
            var snapshot = await host.SnapshotAsync("upgraded-runtime.sqlite.gz", "template-design-upgrade");
            var designSnapshot = await host.SnapshotAsync("upgraded-design.sqlite.gz", "template-design-upgrade", design: true);
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var output = Path.Combine(evidence, "template-design-upgrade"); Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new
                {
                    kind = "actual-cumulative-template-writer-upgrade", sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"),
                    baselineSourceRevision = baseline["sourceRevision"]!.GetValue<string>(), processes,
                    originalHistory, recoveredTemplate, created, recovered, delivery, completed, snapshot, designSnapshot,
                    binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
            }
        }
        finally { Directory.Delete(input, recursive: true); }
    }

    private static async Task<List<string>> DesignHistoryAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId,ProductVersion FROM __EFMigrationsHistory_ElsaWorkflowsDesign ORDER BY MigrationId";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0) + "|" + reader.GetString(1));
        return rows;
    }
}
