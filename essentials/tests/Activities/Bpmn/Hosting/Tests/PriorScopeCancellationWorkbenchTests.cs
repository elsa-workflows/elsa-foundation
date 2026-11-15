using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class PriorScopeCancellationWorkbenchTests
{
    [Fact]
    public async Task The_standard_host_recovers_an_original_queued_result_after_prior_scope_cancellation_and_a_second_cold_start()
    {
        const string outer = "bpmn:723068f9ada40fbde52746a0678a2f5c65e61ef6831c683f85b0098a3daaa044";
        const string middle = "wfexec:start:v1:d02cb25356a4825ec03647642c84fc5028d20300f53f65d3b8ea86a6a5044794";
        const string inner = "bpmn-embedded:57443f3f73b36e4b4cb155ba015275d92664c3a3637cfa4d77f7be40c2f1bcfc";
        const string cancelSource = "commit:round9:0ce3b59c12b34687b8e8c9af19979c01:activity-scope-cancelled:14oD2dLayY2:bpmn-embedded-cancel:e8559ceb6650ed55da14d57a1e5f6fae28b2a9393e60180403bb40be41374a16";
        const string resultKind = "Elsa.Bpmn.NativeWorkResult.v1";
        var temporary = Directory.CreateTempSubdirectory("elsa-prior-scope-upgrade-");
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "prior-scope-cancel", "queued-result.sqlite.gz");
            Assert.Equal("87a7a76fdc45f7cc7c2c0a4f1ac94361c8108a33c1cc1633cafcc49f7487773c", Hash(await File.ReadAllBytesAsync(fixture)));
            var original = Path.Combine(temporary.FullName, "original.sqlite");
            await using (var input = File.OpenRead(fixture))
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(original)) await gzip.CopyToAsync(output);
            Assert.Equal("f19abce8444b42284483f511018039a48e39dd4d9cd6a53ab348a55191f4ecb4", Hash(await File.ReadAllBytesAsync(original)));
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true,
                runtimeSnapshotPath: original, recoverySweepSeconds: 0.2);
            LegacyUpgradeInspection.AssertStandardHost(host);
            var before = await host.ReadEmbeddingAsync(inner, middle, outer, original);
            Assert.Equal((int)RuntimeModelLifecycle.Running, before["directCounterpart"]!["lifecycle"]!.GetValue<int>());
            Assert.Equal((int)RuntimeModelLifecycle.TerminalPendingSettlement, before["model"]!["lifecycle"]!.GetValue<int>());
            Assert.Null(Source(before, cancelSource)["deliveredAt"]);
            var oldResult = Assert.Single(before["sources"]!.AsArray(), item => item!["intent"]!["kind"]!.GetValue<string>() == resultKind)!;
            var resultSource = oldResult["outboxItemId"]!.GetValue<string>();
            Assert.Empty(oldResult["intent"]!["payload"]!["cancellationSources"]!.AsArray());
            await host.LoginAsync();
            var first = host.ProcessId;
            JsonNode completed = null!;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(100);
            while (true)
            {
                completed = await host.ReadEmbeddingAsync(inner, middle, outer);
                if (completed["directCounterpart"]!["lifecycle"]!.GetValue<int>() == (int)RuntimeModelLifecycle.TerminalSettled &&
                    completed["model"]!["lifecycle"]!.GetValue<int>() == (int)RuntimeModelLifecycle.TerminalSettled &&
                    Source(completed, cancelSource)["status"]!.GetValue<int>() == (int)RuntimePostCommitOutboxStatus.Delivered &&
                    Source(completed, resultSource)["status"]!.GetValue<int>() == (int)RuntimePostCommitOutboxStatus.Delivered) break;
                if (DateTimeOffset.UtcNow >= deadline) throw host.Failure("The original queued result did not converge with its prior cancellation: " + completed);
                await Task.Delay(200);
            }
            foreach (var model in new[] { "model", "directCounterpart" })
            {
                foreach (var field in new[] { "scope", "authority", "executable", "materialIncarnationId", "protocol" })
                    Assert.True(JsonNode.DeepEquals(before[model]![field], completed[model]![field]), model + "/" + field);
                Assert.Equal("Cancelled", completed[model]!["terminalResult"]!["status"]!.GetValue<string>());
                Assert.True(JsonNode.DeepEquals(before[model]!["payload"]!["state"], completed[model]!["payload"]!["state"]));
            }
            Assert.True(JsonNode.DeepEquals(before["nativeParent"], completed["nativeParent"]));
            var consumed = Assert.Single(completed["directCounterpart"]!["payload"]!["nativeResults"]!.AsArray())!;
            Assert.True(JsonNode.DeepEquals(oldResult["intent"]!["payload"], consumed));
            Assert.True(Source(completed, cancelSource)["deliveredAt"]!.GetValue<DateTimeOffset>() <=
                completed["directCounterpart"]!["updatedAt"]!.GetValue<DateTimeOffset>());
            Assert.True(completed["directCounterpart"]!["updatedAt"]!.GetValue<DateTimeOffset>() <=
                Source(completed, resultSource)["deliveredAt"]!.GetValue<DateTimeOffset>());
            Assert.Empty(completed["bookmarks"]!.AsArray());
            Assert.Empty(completed["queuedModelInputs"]!.AsArray());
            var api = await host.GetAsync("/bpmn/executions/" + outer, HttpStatusCode.NotFound);
            Assert.Equal(404, api["status"]!.GetValue<int>());
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); var firstExit = host.LastStoppedExitCode;
            var completedStore = await host.SnapshotAsync("completed-runtime.sqlite.gz", "prior-scope-cancel");
            await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync();
            var second = host.ProcessId;
            Assert.NotEqual(first, second);
            var retained = await host.ReadEmbeddingAsync(inner, middle, outer);
            Assert.True(JsonNode.DeepEquals(completed, retained));
            Assert.True(JsonNode.DeepEquals(api, await host.GetAsync("/bpmn/executions/" + outer, HttpStatusCode.NotFound)));
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); var secondExit = host.LastStoppedExitCode;
            var retainedStore = await host.SnapshotAsync("retained-runtime.sqlite.gz", "prior-scope-cancel");
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires exact source.");
                var folder = Path.Combine(evidence, "prior-scope-cancel"); Directory.CreateDirectory(folder);
                foreach (var item in new[] { ("before.json", before), ("completed.json", completed), ("retained.json", retained), ("api.json", api) })
                    await File.WriteAllTextAsync(Path.Combine(folder, item.Item1), item.Item2.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllTextAsync(Path.Combine(folder, "case.json"), JsonSerializer.Serialize(new
                {
                    sourceRevision = source, first, second, firstExit, secondExit, completedStore, retainedStore, cancelSource, resultSource,
                    fixtureSha256 = Hash(await File.ReadAllBytesAsync(fixture)), binaries = host.DescribeBinaries(source), runtimeOnly = true,
                    scopeLimit = "Standard runtime-only SQLite host recovers a derived published SDK fixture's original queued result. No new ETL, Studio, other-provider or broader program acceptance."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { Directory.Delete(temporary.FullName, recursive: true); }

        static JsonNode Source(JsonNode view, string id) => Assert.Single(view["sources"]!.AsArray(), item => item!["outboxItemId"]!.GetValue<string>() == id)!;
        static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
