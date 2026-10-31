using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyTimerAdmissionWorkbenchTests
{
    [Fact]
    public async Task An_actual_accepted_impossible_timer_gets_a_durable_fault_and_normal_work_recovers_after_process_loss()
    {
        var temporary = Directory.CreateTempSubdirectory("elsa-old-timer-");
        try
        {
            var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-timer");
            var fixture = Path.Combine(fixtureRoot, "old-unrepresentable-start.snapshot.sqlite.gz");
            var captured = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "original-case.json")))!;
            Assert.Equal(captured["fixtureSha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture))));
            var original = Path.Combine(temporary.FullName, "original.sqlite");
            await using (var input = File.OpenRead(fixture))
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(original)) await gzip.CopyToAsync(output);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true,
                runtimeSnapshotPath: original, recoverySweepSeconds: 0.2);
            LegacyUpgradeInspection.AssertStandardHost(host);
            await host.LoginAsync();
            var id = captured["executionId"]!.GetValue<string>();
            var path = "/bpmn/executions/" + id;
            var limit = DateTimeOffset.UtcNow.AddSeconds(60);
            JsonNode? recovered;
            while ((recovered = await host.FindAsync(path))?["status"]?.GetValue<string>() != "TerminalSettled")
            {
                if (DateTimeOffset.UtcNow >= limit) throw host.Failure("The actual accepted timer input has no inspectable terminal disposition.");
                await Task.Delay(200);
            }
            Assert.Equal(1, recovered["revision"]!.GetValue<long>());
            Assert.Equal("Faulted", recovered["result"]!["status"]!.GetValue<string>());
            Assert.Equal("bpmn.duration-deadline-unrepresentable", recovered["result"]!["code"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(captured["published"]!["executable"], recovered["executable"]));
            Assert.Equal(captured["published"]!["incarnationId"]!.GetValue<string>(), recovered["materialIncarnationId"]!.GetValue<string>());
            var commandPath = path + "/commands/" + captured["inputId"]!.GetValue<string>();
            var receipt = await host.GetAsync(commandPath);
            Assert.False(receipt["pending"]!.GetValue<bool>());
            Assert.Equal("Accepted", receipt["result"]!["status"]!.GetValue<string>());
            var physical = await ObserveAsync(id);
            Assert.Empty(physical["queuedModelInputs"]!.AsArray());
            Assert.Empty(physical["sources"]!.AsArray());
            Assert.Empty(physical["model"]!["payload"]!["work"]!.AsArray());
            var lateCancel = await host.PostAsync(path + "/cancel", new { requestId = "cancel-after-host-fault", reason = "supported terminal command" });
            Assert.Equal("Rejected", lateCancel["result"]!["status"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(recovered, await host.GetAsync(path)));

            var definition = captured["definition"]!.DeepClone();
            await host.PostAsync("/bpmn/artifacts", new { requestId = "refuse-new-impossible", definition }, HttpStatusCode.UnprocessableEntity);
            var timer = Assert.Single(definition["process"]!["elements"]!.AsArray(), element => element!["elementId"]!.GetValue<string>() == "timeout")!;
            timer["eventDefinitions"]![0]!["properties"]!["interval"] = "PT1H";
            var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-normal-after-repair", definition });
            var start = await host.PostAsync("/bpmn/executions", new { requestId = "normal-after-repair", sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
            var normal = start["executionId"]!.GetValue<string>();
            var normalPath = "/bpmn/executions/" + normal;
            var waiting = await host.GetAsync(normalPath);
            var work = Assert.Single((await host.GetAsync(normalPath + "/work"))["items"]!.AsArray())!;
            var first = host.ProcessId;
            await host.StopAsync();
            var firstExit = host.LastStoppedExitCode;
            var firstStore = await host.SnapshotAsync("fault-and-wait-runtime.sqlite.gz", "old-unrepresentable-timer");
            await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync();
            var second = host.ProcessId;
            Assert.NotEqual(first, second);
            Assert.True(JsonNode.DeepEquals(recovered, await host.GetAsync(path)));
            Assert.True(JsonNode.DeepEquals(receipt, await host.GetAsync(commandPath)));
            Assert.True(JsonNode.DeepEquals(waiting, await host.GetAsync(normalPath)));
            Assert.True(JsonNode.DeepEquals(work, Assert.Single((await host.GetAsync(normalPath + "/work"))["items"]!.AsArray())));
            await host.PostAsync(normalPath + "/work/" + work["invocationId"]!.GetValue<string>() + "/complete",
                new { requestId = "complete-normal-after-restart", attemptId = work["attemptId"]!.GetValue<string>() });
            var completed = await host.GetAsync(normalPath);
            Assert.Equal("TerminalSettled", completed["status"]!.GetValue<string>());
            Assert.Equal("Completed", completed["result"]!["status"]!.GetValue<string>());
            await host.StopAsync();
            var secondExit = host.LastStoppedExitCode;
            var completedStore = await host.SnapshotAsync("completed-runtime.sqlite.gz", "old-unrepresentable-timer");
            await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync();
            var third = host.ProcessId;
            Assert.True(JsonNode.DeepEquals(recovered, await host.GetAsync(path)));
            Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(normalPath)));
            await host.StopAsync();
            var thirdExit = host.LastStoppedExitCode;
            var retainedStore = await host.SnapshotAsync("retained-runtime.sqlite.gz", "old-unrepresentable-timer");
            Assert.NotEqual(0, firstExit); Assert.NotEqual(0, secondExit); Assert.NotEqual(0, thirdExit);
            var evidence = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
            if (evidence is not null)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires exact source.");
                var folder = Path.Combine(evidence, "old-unrepresentable-timer"); Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "case.json"), JsonSerializer.Serialize(new { sourceRevision = source,
                    originalWriter = captured["writerSource"]!.GetValue<string>(), originalFixtureSha256 = captured["fixtureSha256"]!.GetValue<string>(),
                    recovered, receipt, physical, normal, waiting, work, completed, first, second, third, firstExit, secondExit, thirdExit,
                    firstStore, completedStore, retainedStore, binaries = host.DescribeBinaries(source), runtimeOnly = true,
                    scope = "Actual source9c accepted input, supported terminal inspection/command and normal direct wait/restart/resume/complete; SQLite, no Studio or other-provider claim" }, new JsonSerializerOptions { WriteIndented = true }));
            }

            async Task<JsonNode> ObserveAsync(string execution)
            {
                var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { host.ReaderAssemblyPath, "--read-bpmn-direct", host.RuntimeDatabase, execution }) startInfo.ArgumentList.Add(arg);
                using var observer = Process.Start(startInfo) ?? throw new InvalidOperationException("The owned read-only observer did not start.");
                var output = observer.StandardOutput.ReadToEndAsync(); var error = observer.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await observer.WaitForExitAsync(timeout.Token); }
                catch { if (!observer.HasExited) { observer.Kill(entireProcessTree: true); await observer.WaitForExitAsync(); } throw; }
                if (observer.ExitCode != 0) throw host.Failure("The read-only observer failed: " + await error + await output);
                return JsonNode.Parse(await output)!;
            }
        }
        finally { temporary.Delete(recursive: true); }
    }
}
