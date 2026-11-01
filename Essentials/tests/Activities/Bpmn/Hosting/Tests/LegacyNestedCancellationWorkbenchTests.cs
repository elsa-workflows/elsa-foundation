using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyNestedCancellationWorkbenchTests
{
    [Fact]
    public async Task An_actual_pending_nested_cancellation_settles_on_a_fresh_runtime_only_host_and_stays_settled()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-nested-cancel");
        var capture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "process.json")))!;
        var oldOuter = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "after-cancel-outer-physical.json")))!;
        var oldInner = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "after-cancel-inner-physical.json")))!;
        var oldCommand = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "cancel-response.json")))!;
        Assert.Equal("9508e25e57672c569f462aff9a1e0282c5780eb9", capture["sourceRevision"]!.GetValue<string>());
        Assert.Equal(2, oldOuter["model"]!["lifecycle"]!.GetValue<int>());
        Assert.Equal(3, oldInner["model"]!["lifecycle"]!.GetValue<int>());
        var original = Directory.CreateTempSubdirectory("elsa-old-nested-cancel-");
        try
        {
            foreach (var store in new[] { "elsa", "etl-runtime", "etl-design" })
            {
                var compressed = Path.Combine(fixtures, "after-cancel-" + store + ".sqlite.gz");
                Assert.Equal(capture["afterCancelStores"]![store]!["gzipSha256"]!.GetValue<string>(),
                    Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(compressed))));
                await using var input = new GZipStream(File.OpenRead(compressed), CompressionMode.Decompress);
                await using var output = File.Create(Path.Combine(original.FullName, store + ".sqlite"));
                await input.CopyToAsync(output);
            }
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true,
                runtimeSnapshotPath: Path.Combine(original.FullName, "elsa.sqlite"), recoverySweepSeconds: 0.2);
            LegacyUpgradeInspection.AssertStandardHost(host);
            await host.LoginAsync();
            var outer = capture["outerModelExecutionId"]!.GetValue<string>();
            var inner = capture["innerModelExecutionId"]!.GetValue<string>();
            var native = capture["middleNativeExecutionId"]!.GetValue<string>();
            var limit = DateTimeOffset.UtcNow.AddSeconds(60);
            JsonNode? completed;
            while ((completed = await host.FindAsync("/bpmn/executions/" + outer))?["status"]?.GetValue<string>() != "TerminalSettled")
            {
                if (DateTimeOffset.UtcNow >= limit) throw host.Failure("The original nested cancellation remains pending after cold recovery.");
                await Task.Delay(100);
            }
            Assert.Equal("Cancelled", completed["result"]!["status"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(oldOuter["model"]!["executable"], completed["executable"]));
            Assert.Equal(oldOuter["model"]!["materialIncarnationId"]!.GetValue<string>(), completed["materialIncarnationId"]!.GetValue<string>());
            var innerCompleted = await host.GetAsync("/bpmn/executions/" + inner);
            Assert.Equal("TerminalSettled", innerCompleted["status"]!.GetValue<string>());
            Assert.Equal("Cancelled", innerCompleted["result"]!["status"]!.GetValue<string>());
            var command = await host.GetAsync("/bpmn/executions/" + outer + "/commands/cancel-nested-root");
            Assert.True(JsonNode.DeepEquals(oldCommand["result"], command["result"]));
            JsonNode physical;
            JsonNode direct;
            do
            {
                physical = await ObserveAsync();
                direct = await ObserveAsync(directModel: true);
                if (physical["resultSource"]?["status"]?.GetValue<int>() == 2 &&
                    direct["sources"]!.AsArray().All(source => source!["status"]!.GetValue<int>() == 2) &&
                    direct["queuedModelInputs"]!.AsArray().Count == 0) break;
                if (DateTimeOffset.UtcNow >= limit) throw host.Failure("Nested cancellation consumption has not acknowledged every original source.");
                await Task.Delay(100);
            } while (true);
            var first = host.ProcessId;
            await host.StopAsync();
            var firstExit = host.LastStoppedExitCode;
            Assert.True(JsonNode.DeepEquals(physical, await ObserveAsync()));
            Assert.True(JsonNode.DeepEquals(direct, await ObserveAsync(directModel: true)));
            Assert.Equal(3, physical["model"]!["lifecycle"]!.GetValue<int>());
            Assert.True(Assert.Single(physical["model"]!["payload"]!["cancellations"]!.AsArray())!["settled"]!.GetValue<bool>());
            Assert.NotNull(physical["resultInput"]);
            Assert.Equal(2, physical["resultSource"]!["status"]!.GetValue<int>());
            var firstStore = await host.SnapshotAsync("recovered-runtime.sqlite.gz", "nested-cancel");
            await host.RestartAsync();
            await host.LoginAsync();
            var second = host.ProcessId;
            Assert.NotEqual(first, second);
            Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync("/bpmn/executions/" + outer)));
            Assert.True(JsonNode.DeepEquals(innerCompleted, await host.GetAsync("/bpmn/executions/" + inner)));
            Assert.True(JsonNode.DeepEquals(command, await host.GetAsync("/bpmn/executions/" + outer + "/commands/cancel-nested-root")));
            await host.StopAsync();
            var secondExit = host.LastStoppedExitCode;
            Assert.True(JsonNode.DeepEquals(physical, await ObserveAsync()));
            Assert.True(JsonNode.DeepEquals(direct, await ObserveAsync(directModel: true)));
            var retainedStore = await host.SnapshotAsync("retained-runtime.sqlite.gz", "nested-cancel");
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var directory = Directory.CreateDirectory(Path.Combine(evidence, "nested-cancel")).FullName;
                await File.WriteAllTextAsync(Path.Combine(directory, "case.json"), JsonSerializer.Serialize(new
                {
                    sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), originalWriter = capture["sourceRevision"],
                    outer, inner, native, completed, innerCompleted, command, physical, direct, first, second, firstExit, secondExit,
                    firstStore, retainedStore, binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")),
                    runtimeOnly = true, etlStoresOpened = false,
                    scope = "Actual source9508 outer-pending/inner-settled cancellation recovery. Original coupled ETL stores remain offline; alternative pending-child delivery order and broader ETL/Studio/provider acceptance are separate."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            async Task<JsonNode> ObserveAsync(bool directModel = false)
            {
                var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { host.ReaderAssemblyPath, directModel ? "--read-bpmn-direct" : "--read-bpmn-native", host.RuntimeDatabase, outer }) info.ArgumentList.Add(arg);
                if (!directModel) info.ArgumentList.Add(native);
                using var reader = Process.Start(info) ?? throw host.Failure("The closed-store nested cancellation reader did not start.");
                var output = reader.StandardOutput.ReadToEndAsync();
                var error = reader.StandardError.ReadToEndAsync();
                await reader.WaitForExitAsync();
                Assert.Equal(0, reader.ExitCode);
                var result = JsonNode.Parse(await output)!;
                Assert.True(result["readOnly"]!.GetValue<bool>(), await error);
                return result;
            }
        }
        finally { original.Delete(true); }
    }
}
