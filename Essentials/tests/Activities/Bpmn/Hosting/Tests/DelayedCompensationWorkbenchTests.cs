using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class DelayedCompensationWorkbenchTests
{
    [Fact]
    public async Task The_standard_host_recovers_the_original_cancelled_result_with_obsolete_native_retry_and_retains_it_after_a_cold_start()
    {
        const string model = "bpmn:a29cc5c675038cc802eabef05ca3c36a517ddbf17c66304badea80379b1e56cd";
        const string resultKind = "Elsa.Bpmn.NativeWorkResult.v1";
        var temporary = Directory.CreateTempSubdirectory("elsa-delayed-compensation-upgrade-");
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "delayed-compensation", "queued-result.sqlite.gz");
            var original = Path.Combine(temporary.FullName, "original.sqlite");
            await using (var input = File.OpenRead(fixture))
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(original)) await gzip.CopyToAsync(output);
            Assert.Equal("dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63", Hash(await File.ReadAllBytesAsync(original)));
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true,
                runtimeSnapshotPath: original, recoverySweepSeconds: 0.2);
            LegacyUpgradeInspection.AssertStandardHost(host);
            var before = await ObserveAsync(original);
            Assert.Equal((int)RuntimeModelLifecycle.TerminalPendingSettlement, before["model"]!["lifecycle"]!.GetValue<int>());
            Assert.Equal((int)WorkflowExecutionStatus.Cancelled, before["worker"]!["status"]!.GetValue<int>());
            var oldResult = Assert.Single(before["sources"]!.AsArray(), x => x!["intent"]!["kind"]!.GetValue<string>() == resultKind)!;
            var resultSource = oldResult["outboxItemId"]!.GetValue<string>();
            var retry = Assert.Single(before["sources"]!.AsArray(), x => x!["intent"]!["kind"]!.GetValue<string>() == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
                x["status"]!.GetValue<int>() != (int)RuntimePostCommitOutboxStatus.Delivered)!;
            var retrySource = retry["outboxItemId"]!.GetValue<string>();
            var retryDue = retry["availableAt"]!.GetValue<DateTimeOffset>();
            var observedAt = DateTimeOffset.UtcNow;
            var originalInput = Assert.Single(before["queuedModelInputs"]!.AsArray())!["commandId"]!.GetValue<string>();
            await host.LoginAsync(); var first = host.ProcessId;
            JsonNode completed = null!;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (true)
            {
                completed = await ObserveAsync(host.RuntimeDatabase);
                if (completed["model"]!["lifecycle"]!.GetValue<int>() == (int)RuntimeModelLifecycle.TerminalSettled &&
                    completed["sources"]!.AsArray().Where(x => x!["intent"]!["kind"]!.GetValue<string>().StartsWith("Elsa.Bpmn.", StringComparison.Ordinal))
                        .All(x => x!["status"]!.GetValue<int>() == (int)RuntimePostCommitOutboxStatus.Delivered)) break;
                if (DateTimeOffset.UtcNow >= deadline) throw host.Failure("The original cancelled result did not recover: " + completed);
                await Task.Delay(200);
            }
            var completedAt = DateTimeOffset.UtcNow;
            foreach (var field in new[] { "scope", "authority", "executable", "materialIncarnationId", "protocol", "terminalResult" })
                Assert.True(JsonNode.DeepEquals(before["model"]![field], completed["model"]![field]), field);
            Assert.True(JsonNode.DeepEquals(before["model"]!["payload"]!["state"], completed["model"]!["payload"]!["state"]));
            foreach (var field in new[] { "worker", "workerMaterial", "nativeMarkers" })
                Assert.True(JsonNode.DeepEquals(before[field], completed[field]), field);
            Assert.True(JsonNode.DeepEquals(oldResult["intent"]!["payload"], Assert.Single(completed["model"]!["payload"]!["nativeResults"]!.AsArray())));
            Assert.Contains(completed["inputs"]!.AsArray(), x => x!["inputId"]!.GetValue<string>() == originalInput &&
                x["result"]!["status"]!.GetValue<string>() == "Accepted");
            Assert.Empty(completed["queuedModelInputs"]!.AsArray()); Assert.Empty(completed["queuedNativeWork"]!.AsArray());
            // The frozen due time eventually passes in CI. Fresh journey tests always create a new one-hour wait;
            // this old-writer replay must also recover once its ordinary obsolete transport becomes due.
            if (retryDue > completedAt) Assert.True(JsonNode.DeepEquals(retry, Source(completed, retrySource)));
            Assert.True(completed["model"]!["updatedAt"]!.GetValue<DateTimeOffset>() <= Source(completed, resultSource)["deliveredAt"]!.GetValue<DateTimeOffset>());
            var api = await host.GetAsync("/bpmn/executions/" + model, HttpStatusCode.NotFound);
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); var firstExit = host.LastStoppedExitCode;
            var completedStore = await host.SnapshotAsync("completed-runtime.sqlite.gz", "delayed-compensation");
            await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync(); var second = host.ProcessId;
            Assert.NotEqual(first, second);
            var retained = await ObserveAsync(host.RuntimeDatabase);
            foreach (var field in new[] { "model", "worker", "workerMaterial", "nativeMarkers", "inputs", "queuedModelInputs", "queuedNativeWork" })
                Assert.True(JsonNode.DeepEquals(completed[field], retained[field]), field);
            Assert.True(JsonNode.DeepEquals(api, await host.GetAsync("/bpmn/executions/" + model, HttpStatusCode.NotFound)));
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); var secondExit = host.LastStoppedExitCode;
            var retainedStore = await host.SnapshotAsync("retained-runtime.sqlite.gz", "delayed-compensation");
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires exact source.");
                var folder = Path.Combine(evidence, "delayed-compensation"); Directory.CreateDirectory(folder);
                foreach (var item in new[] { ("before.json", before), ("completed.json", completed), ("retained.json", retained), ("api.json", api) })
                    await File.WriteAllTextAsync(Path.Combine(folder, item.Item1), item.Item2.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllTextAsync(Path.Combine(folder, "case.json"), JsonSerializer.Serialize(new
                {
                    sourceRevision = source, first, second, firstExit, secondExit, completedStore, retainedStore, observedAt, completedAt, retryDue,
                    obsoleteRetryStillFutureAtSettlement = retryDue > completedAt, retrySource, resultSource,
                    binaries = host.DescribeBinaries(source), fixtureSha256 = Hash(await File.ReadAllBytesAsync(fixture)), runtimeOnly = true,
                    scopeLimit = "Standard SQLite runtime-only host recovers authentic old SDK queued result; no new Studio, ETL or other-provider acceptance."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            async Task<JsonNode> ObserveAsync(string database)
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { host.ReaderAssemblyPath, "--read-bpmn-retirement", database, model }) start.ArgumentList.Add(arg);
                using var reader = Process.Start(start) ?? throw new InvalidOperationException("The checked recovery observer did not start.");
                var output = reader.StandardOutput.ReadToEndAsync(); var error = reader.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await reader.WaitForExitAsync(timeout.Token); }
                catch { if (!reader.HasExited) { reader.Kill(entireProcessTree: true); await reader.WaitForExitAsync(); } throw; }
                if (reader.ExitCode != 0) throw host.Failure("The checked recovery observer failed: " + await error);
                return JsonNode.Parse(await output)!;
            }
        }
        finally { Directory.Delete(temporary.FullName, recursive: true); }

        static JsonNode Source(JsonNode view, string id) => Assert.Single(view["sources"]!.AsArray(), x => x!["outboxItemId"]!.GetValue<string>() == id)!;
        static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
