using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyFaultSettlementWorkbenchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_standard_runtime_only_host_settles_actual_old_fault_responsibility_and_retains_it_after_process_loss(bool admitted)
    {
        var temporary = Directory.CreateTempSubdirectory("elsa-old-fault-upgrade-");
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-fault", admitted ? "concurrent-admitted-terminal.sqlite.gz" : "concurrent-terminal.sqlite.gz");
            var original = Path.Combine(temporary.FullName, "original.sqlite");
            await using (var input = File.OpenRead(fixture))
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(original)) await gzip.CopyToAsync(output);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true,
                runtimeSnapshotPath: original, recoverySweepSeconds: 0.2);
            LegacyUpgradeInspection.AssertStandardHost(host);
            const string model = "bpmn:29ce72ce3be41205dfa938e934fbac80cb4cca8c3508df1b2dd86fa94dfdc220";
            // Retain the exact normally captured identity; the stores remain isolated.
            var id = admitted ? await CapturedIdAsync() : model;
            var before = await ObserveAsync("--read-bpmn-direct", original, id);
            Assert.Equal(2, before["model"]!["revision"]!.GetValue<long>());
            Assert.Equal(3, before["model"]!["lifecycle"]!.GetValue<int>());
            Assert.Single(before["model"]!["payload"]!["work"]!.AsArray());
            await host.LoginAsync();
            var first = host.ProcessId;
            JsonNode final = null!;
            var limit = DateTimeOffset.UtcNow.AddSeconds(60);
            while (true)
            {
                final = await ObserveAsync("--read-bpmn-direct", host.RuntimeDatabase, id);
                if (final["model"]!["payload"]?["cancellations"] is JsonArray cancellations &&
                    cancellations.All(item => item!["settled"]!.GetValue<bool>()) && final["model"]!["lifecycle"]!.GetValue<int>() == 3 &&
                    final["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2) && final["queuedModelInputs"]!.AsArray().Count == 0) break;
                if (DateTimeOffset.UtcNow >= limit) throw new InvalidOperationException("Actual old fault responsibility did not settle: " + final);
                await Task.Delay(200);
            }
            var truth = await ObserveAsync("--read-bpmn-retirement", host.RuntimeDatabase, id);
            // The normal producer used probe-user, which is not this host's logged-in owner.
            // Preserve that authorization boundary while the standard background host settles its stored work.
            var api = await host.GetAsync("/bpmn/executions/" + id, HttpStatusCode.NotFound);
            Assert.Equal(404, api["status"]!.GetValue<int>());
            Assert.Equal("Faulted", final["model"]!["terminalResult"]!["status"]!.GetValue<string>());
            Assert.Equal(admitted ? 2 : 1, final["nativeWorkflowRows"]!.GetValue<int>());
            Assert.Empty(final["model"]!["payload"]!["work"]!.AsArray());
            foreach (var field in new[] { "scope", "authority", "executable", "materialIncarnationId", "terminalResult" })
                Assert.True(JsonNode.DeepEquals(before["model"]![field], final["model"]![field]), field);
            Assert.True(JsonNode.DeepEquals(before["model"]!["payload"]!["state"], final["model"]!["payload"]!["state"]));
            var cancellation = Assert.Single(final["model"]!["payload"]!["cancellations"]!.AsArray())!;
            Assert.Equal(!admitted, cancellation["notAdmitted"]?.GetValue<bool>() ?? false);
            var receiptKind = admitted ? "native-result" : "native-start-retired";
            Assert.Contains(truth["inputs"]!.AsArray(), input => input!["inputKind"]!.GetValue<string>() == receiptKind && input["result"]!["status"]!.GetValue<string>() == "Accepted");
            Assert.Empty(truth["queuedNativeWork"]!.AsArray());
            Assert.False(File.Exists(host.DesignDatabase));
            var name = admitted ? "old-admitted-fault" : "old-unadmitted-fault";
            await host.StopAsync(); var firstExit = host.LastStoppedExitCode;
            var completedStore = await host.SnapshotAsync("completed-runtime.sqlite.gz", name);
            await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync();
            var second = host.ProcessId;
            Assert.NotEqual(first, second);
            var retained = await ObserveAsync("--read-bpmn-direct", host.RuntimeDatabase, id);
            var retainedTruth = await ObserveAsync("--read-bpmn-retirement", host.RuntimeDatabase, id);
            Assert.True(JsonNode.DeepEquals(final, retained));
            Assert.True(JsonNode.DeepEquals(truth, retainedTruth));
            Assert.True(JsonNode.DeepEquals(api, await host.GetAsync("/bpmn/executions/" + id, HttpStatusCode.NotFound)));
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); var secondExit = host.LastStoppedExitCode;
            Assert.NotEqual(0, firstExit); Assert.NotEqual(0, secondExit);
            var retainedStore = await host.SnapshotAsync("retained-runtime.sqlite.gz", name);
            var evidence = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
            if (evidence is not null)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires exact source.");
                var folder = Path.Combine(evidence, name); Directory.CreateDirectory(folder);
                foreach (var item in new[] { ("before.json", before), ("completed.json", final), ("retained.json", retained), ("truth.json", truth), ("retained-truth.json", retainedTruth), ("api.json", api) })
                    await File.WriteAllTextAsync(Path.Combine(folder, item.Item1), item.Item2.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllTextAsync(Path.Combine(folder, "case.json"), JsonSerializer.Serialize(new { kind = "actual-old-final-fault-workbench-recovery", sourceRevision = source,
                    admitted, modelExecutionId = id, fixtureSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture))),
                    first, second, firstExit, secondExit, runtimeOnly = true, completedStore, retainedStore,
                    binaries = host.DescribeBinaries(source), scopeLimit = "Two actual normally produced old fault cuts over SQLite, standard runtime-only Workbench; no Studio, ETL or other-provider claim." }, new JsonSerializerOptions { WriteIndented = true }));
            }

            async Task<JsonNode> ObserveAsync(string mode, string database, string execution)
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { host.ReaderAssemblyPath, mode, database, execution }) start.ArgumentList.Add(arg);
                using var reader = Process.Start(start) ?? throw new InvalidOperationException("The checked fault observer did not start.");
                var output = reader.StandardOutput.ReadToEndAsync(); var error = reader.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await reader.WaitForExitAsync(timeout.Token); }
                catch { if (!reader.HasExited) { reader.Kill(entireProcessTree: true); await reader.WaitForExitAsync(); } throw; }
                if (reader.ExitCode != 0) throw new InvalidOperationException(await error);
                return JsonNode.Parse(await output)!;
            }
            async Task<string> CapturedIdAsync()
            {
                // This fixture's model key is bound to the producer's ordinary start request and material.
                // The producer logs are retained with the whole snapshot; no database row is changed here.
                var log = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-fault", "admitted-old-writer.log.gz");
                await using var input = File.OpenRead(log); await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var text = new StreamReader(gzip);
                while (await text.ReadLineAsync() is { } line)
                    if (line.StartsWith("START ", StringComparison.Ordinal)) return JsonNode.Parse(line[6..])!["ExecutionId"]!.GetValue<string>();
                throw new InvalidDataException("The normal admitted producer lacks its actual start identity.");
            }
        }
        finally { Directory.Delete(temporary.FullName, recursive: true); }
    }
}
