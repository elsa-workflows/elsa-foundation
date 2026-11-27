using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class SignalCancellationWorkbenchTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public async Task Cancelling_one_actual_Event_worker_retires_the_model_and_settles_its_sibling()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "signal-cancellation-input");
        var input = Directory.CreateTempSubdirectory("elsa-event-cancel-input-").FullName;
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
        const string name = "one-worker-cancel"; string? output = null;
        if (directory is not null)
        {
            Assert.False(string.IsNullOrWhiteSpace(source)); output = Path.Combine(directory, name);
            Assert.False(Directory.Exists(output)); Directory.CreateDirectory(output);
        }
        async Task Save(string file, object value)
        { if (output is not null) await File.WriteAllTextAsync(Path.Combine(output, file), JsonSerializer.Serialize(value, Web) + Environment.NewLine); }
        try
        {
            var inputs = new List<object>();
            foreach (var store in new[] { "runtime", "design" })
            {
                var original = Path.Combine(fixture, "retained-" + store + ".sqlite.gz");
                await using var raw = File.OpenRead(original); await using var zipped = new GZipStream(raw, CompressionMode.Decompress);
                await using var target = File.Create(Path.Combine(input, store + ".sqlite")); await zipped.CopyToAsync(target);
                inputs.Add(new { file = Path.GetFileName(original), sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(original))) });
            }
            var captured = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "late-before-crash.json")))!;
            var identity = captured["model"]!["executionId"]!.GetValue<string>();
            var workers = captured["model"]!["payload"]!["work"]!.AsArray().Select(work => work!["nativeWork"]!["workerExecutionId"]!.GetValue<string>()).ToArray();
            Assert.Equal(2, workers.Length);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: input, recoverySweepSeconds: .2);
            var processes = new List<object>(); var exits = new List<int?>(); var stores = new List<object?>();
            void Record() => processes.Add(new { processId = host.ProcessId, controllerProcessId = Environment.ProcessId,
                startedAtUtc = Process.GetProcessById(host.ProcessId).StartTime.ToUniversalTime() });
            async Task Cut(string stage)
            {
                await host.StopAsync(); exits.Add(host.LastStoppedExitCode);
                stores.Add(await host.SnapshotAsync(stage + "-runtime.sqlite.gz", name));
                stores.Add(await host.SnapshotAsync(stage + "-design.sqlite.gz", name, design: true));
            }
            Record(); var stage = "restored-wait";
            try
            {
                await host.LoginAsync(); var route = "/bpmn/executions/" + identity;
                var before = await host.GetAsync(route); Assert.Equal("Running", before["status"]!.GetValue<string>());
                Assert.Equal(1, before["revision"]!.GetValue<long>());
                await Save("before.json", before);
                await Cut("waiting"); await host.RestartAsync(); Record(); await host.LoginAsync();
                Assert.True(JsonNode.DeepEquals(before, await host.GetAsync(route))); stage = "cancel-one-worker";
                var plan = await host.PostAsync("/runtime/workflows/alteration-plans", new
                {
                    target = new { executionIds = new[] { workers[0] } },
                    alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
                }, idempotencyKey: "cancel-one-real-event-worker");
                var finished = await Wait(() => host.GetAsync(route), value => value["status"]!.GetValue<string>() == "TerminalSettled");
                var native = new List<JsonNode>();
                foreach (var worker in workers) native.Add(await host.GetAsync("/runtime/workflows/instances/" + worker));
                await Save("after.json", new { model = finished, native, plan });
                await Cut("finished");
                Assert.Equal("Cancelled", finished["result"]!["status"]!.GetValue<string>());
                Assert.All(native, value => Assert.Equal("Cancelled", value["instance"]!["status"]!.GetValue<string>()));
                await host.RestartAsync(); Record(); await host.LoginAsync(); stage = "retained";
                var retained = await host.GetAsync(route); Assert.True(JsonNode.DeepEquals(finished, retained));
                foreach (var worker in workers) Assert.Equal("Cancelled", (await host.GetAsync("/runtime/workflows/instances/" + worker))["instance"]!["status"]!.GetValue<string>());
                await Save("retained.json", retained); await Cut("retained");
                await Save("process.json", new { schemaVersion = 1, kind = "actual-event-worker-sibling-cancellation", sourceRevision = source,
                    modelExecutionId = identity, workers, cancelledWorker = workers[0], inputs, processes, exits, stores,
                    binaries = host.DescribeBinaries(source), restoredFromActualSource = "791dc17016a6b6022a0413820fe61eb202c52afc", stateSeeded = false });
            }
            catch (Exception exception)
            {
                await host.StopAsync();
                await Save("failure.json", new { stage, sourceRevision = source, type = exception.GetType().FullName, modelExecutionId = identity, workers,
                    inputs, processes, exits, stores, qualifiesJourney = false });
                throw;
            }
        }
        finally { Directory.Delete(input, recursive: true); }
    }

    private static async Task<JsonNode> Wait(Func<Task<JsonNode>> read, Func<JsonNode, bool> ready)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(90)) { var value = await read(); if (ready(value)) return value; await Task.Delay(200); }
        throw new TimeoutException("The actual Event-worker cancellation did not settle.");
    }
}
