using System.Net;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1192: the completion request and continuous inspection through the runtime HTTP API.</summary>
public sealed partial class EtlRuntimeApiHttpTests
{
    private static readonly string[] CompletePermissions = [EtlRuntimePermissions.CompleteRuns];

    [Fact]
    public async Task Runtime_http_complete_requires_its_permission_wakes_only_idle_shards_and_reports_each_result()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var continuous = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(600) };
        var admission = await SeedConnectorAsync(host, "continuous-api", scope, continuous);
        await using (var services = host.Host.Services.CreateAsyncScope())
        {
            var store = services.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
            // Shard 0 catches up and idles; shard 1 waits on a 429 retry.
            var idle = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, admission.Invocation.RunId, "worker"));
            var begin = await store.BeginConnectorHttpSourceAsync(idle, Guid.NewGuid().ToString("N"));
            Assert.Equal(new EtlConnectorHttpCaughtUpResult(EtlStoreStatus.Applied, false),
                await store.RecordConnectorHttpCaughtUpAsync(idle, begin.Permit!, DateTimeOffset.UtcNow));
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(idle));
            var waiting = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, admission.Invocation.RunId, "worker"));
            var retry = await store.BeginConnectorHttpSourceAsync(waiting, Guid.NewGuid().ToString("N"));
            Assert.Equal(EtlStoreStatus.Applied, (await store.CompleteConnectorHttpSourceAsync(waiting, retry.Permit!, new(429, null, false))).Status);
            Assert.NotEqual(idle.PartitionOrdinal, waiting.PartitionOrdinal);
        }

        using (var detail = await ReadJsonAsync(await host.SendAsync(HttpMethod.Get, "/etl/runs/continuous-api", permissions: ReadPermissions)))
        {
            var connector = detail.RootElement.GetProperty("connector");
            Assert.Equal("continuous", RequiredString(connector, "mode"));
            Assert.Equal(JsonValueKind.Null, connector.GetProperty("completionRequestedAt").ValueKind);
        }
        var (idleBefore, retryBefore) = await ReadContinuousPartitionsAsync(host, "continuous-api");
        Assert.Equal(idleBefore.GetProperty("notBefore").GetString(), idleBefore.GetProperty("nextPollAt").GetString());
        Assert.Equal(JsonValueKind.Null, retryBefore.GetProperty("nextPollAt").ValueKind);
        var retryNotBefore = retryBefore.GetProperty("notBefore").GetString();
        Assert.NotNull(retryNotBefore);

        using var denied = await host.SendAsync(HttpMethod.Post, "/etl/runs/continuous-api/complete", permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var foreign = await host.SendAsync(HttpMethod.Post, "/etl/runs/continuous-api/complete",
            tenant: "tenant-b", partition: "partition-a", permissions: CompletePermissions);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "etl.run-not-found");

        Assert.Equal("accepted", await CompleteResultAsync(host, "continuous-api"));
        var (idleAfter, retryAfter) = await ReadContinuousPartitionsAsync(host, "continuous-api");
        Assert.Equal(JsonValueKind.Null, idleAfter.GetProperty("notBefore").ValueKind);
        Assert.Equal(JsonValueKind.Null, idleAfter.GetProperty("nextPollAt").ValueKind);
        Assert.Equal(retryNotBefore, retryAfter.GetProperty("notBefore").GetString());
        using (var detail = await ReadJsonAsync(await host.SendAsync(HttpMethod.Get, "/etl/runs/continuous-api", permissions: ReadPermissions)))
            Assert.Equal(JsonValueKind.String, detail.RootElement.GetProperty("connector").GetProperty("completionRequestedAt").ValueKind);
        Assert.Equal("alreadyRequested", await CompleteResultAsync(host, "continuous-api"));

        await SeedConnectorAsync(host, "finite-api", scope);
        Assert.Equal("notContinuous", await CompleteResultAsync(host, "finite-api"));
        using (var finite = await ReadJsonAsync(await host.SendAsync(HttpMethod.Get, "/etl/runs/finite-api", permissions: ReadPermissions)))
            Assert.Equal("finite", RequiredString(finite.RootElement.GetProperty("connector"), "mode"));

        using var cancelled = await host.SendAsync(HttpMethod.Post, "/etl/runs/continuous-api/cancel", permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("terminal", await CompleteResultAsync(host, "continuous-api"));
        using var missing = await host.SendAsync(HttpMethod.Post, "/etl/runs/not-found/complete", permissions: CompletePermissions);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "etl.run-not-found");
    }

    private static async Task<string> CompleteResultAsync(EtlDesignApiTestHost host, string runId)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{runId}/complete", permissions: CompletePermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        return RequiredString(document.RootElement, "result");
    }

    private static async Task<(JsonElement Idle, JsonElement Retry)> ReadContinuousPartitionsAsync(EtlDesignApiTestHost host, string runId)
    {
        using var response = await host.SendAsync(HttpMethod.Get, $"/etl/runs/{runId}/connector-partitions?limit=10", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        var items = document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();
        // The idle shard has no fetch journal; the retry wait reports its waiting journal.
        return (items.Single(item => item.GetProperty("http").GetProperty("fetch").ValueKind == JsonValueKind.Null && item.GetProperty("partitionOrdinal").GetInt32() == 0),
            items.Single(item => item.GetProperty("http").GetProperty("fetch").ValueKind == JsonValueKind.Object));
    }
}
