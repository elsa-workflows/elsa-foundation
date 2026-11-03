using System.Net;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime.Api;
using Elsa.Etl.Runtime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            Assert.Equal(new EtlHttpCaughtUpResult(EtlStoreStatus.Applied, false),
                await store.RecordConnectorHttpCaughtUpAsync(idle, begin.Permit!, DateTimeOffset.UtcNow));
            // Before the worker releases its lease, the idle shard is still claimed and inspection must accept that.
            using (var claimed = await ReadJsonAsync(await host.SendAsync(HttpMethod.Get,
                       "/etl/runs/continuous-api/connector-partitions?limit=10", permissions: ReadPermissions)))
            {
                var row = claimed.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("partitionOrdinal").GetInt32() == idle.PartitionOrdinal);
                Assert.True(row.GetProperty("isClaimed").GetBoolean());
                Assert.Equal(row.GetProperty("notBefore").GetString(), row.GetProperty("nextPollAt").GetString());
            }
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

    [Fact]
    public async Task Caught_up_records_need_the_live_lease_and_a_continuous_run_and_preparing_runs_accept_completion()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var continuous = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(600) };
        var admission = await SeedConnectorAsync(host, "continuous-store", scope, continuous);
        var finite = await SeedConnectorAsync(host, "finite-store", scope);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();

        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, admission.Invocation.RunId, "worker"));
        var permit = (await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"))).Permit!;
        Assert.Equal(EtlStoreStatus.LeaseLost, (await store.RecordConnectorHttpCaughtUpAsync(lease with { Fence = lease.Fence + 1 }, permit, DateTimeOffset.UtcNow)).Status);
        Assert.Equal(EtlStoreStatus.Conflict, (await store.RecordConnectorHttpCaughtUpAsync(lease, permit with { RequestId = Guid.NewGuid().ToString("N") }, DateTimeOffset.UtcNow)).Status);
        Assert.Equal(EtlStoreStatus.Invalid, (await store.RecordConnectorHttpCaughtUpAsync(lease, permit, permit.StartedAt.AddSeconds(-1))).Status);

        var finiteLease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, finite.Invocation.RunId, "worker"));
        var finitePermit = (await store.BeginConnectorHttpSourceAsync(finiteLease, Guid.NewGuid().ToString("N"))).Permit!;
        Assert.Equal(EtlStoreStatus.Invalid, (await store.RecordConnectorHttpCaughtUpAsync(finiteLease, finitePermit, DateTimeOffset.UtcNow)).Status);

        // Completion can be recorded while the run is still preparing.
        var preparing = EtlRuntimeFixtures.ConnectorAdmission("continuous-preparing", scope, continuous, ["private-shard-one"]);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(preparing)).Status);
        Assert.Equal(EtlRunCompletionOutcome.Accepted, await store.RequestConnectorRunCompletionAsync(scope, preparing.Invocation.RunId));
    }

    [Fact]
    public async Task A_finite_partition_may_not_wait_without_a_retry_journal()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var finite = await SeedConnectorAsync(host, "finite-wait", scope);
        await using var services = host.Host.Services.CreateAsyncScope();
        // Only a continuous run's caught-up shard may carry an eligibility time with a cleared journal.
        await services.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE elsa_etl_connector_partitions SET NotBeforeUtcTicks = 1 WHERE PartitionOrdinal = 0;");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await services.ServiceProvider
            .GetRequiredService<IEtlConnectorRuntimeInspector>().ListConnectorPartitionsAsync(scope, finite.Invocation.RunId, -1, 10));
    }

    [Fact]
    public async Task Completion_reports_not_continuous_without_a_connector_store_and_unavailable_for_legacy_operations()
    {
        await using (var fileOnly = await EtlDesignApiTestHost.StartRuntimeAsync(configureServices: services =>
                         services.RemoveAll<IEtlConnectorRuntimeStore>()))
        {
            var scope = new EtlScope("tenant-a", "partition-a");
            var invocation = await SeedRunningAsync(fileOnly, "file-only-complete", scope);
            Assert.Equal("notContinuous", await CompleteResultAsync(fileOnly, invocation.RunId));

            // A v2 run cannot be judged without the connector store, so the API reports it unavailable rather than finite.
            var admission = EtlRuntimeFixtures.ConnectorAdmission("v2-without-store", scope,
                EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(600) }, ["private-shard-one"]);
            await using (var services = fileOnly.Host.Services.CreateAsyncScope())
            {
                var connectors = (IEtlConnectorRuntimeStore)services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
                Assert.Equal(EtlStoreStatus.Applied, (await connectors.AdmitConnectorAsync(admission)).Status);
            }
            using var v2WithoutStore = await fileOnly.SendAsync(HttpMethod.Post, "/etl/runs/v2-without-store/complete", permissions: CompletePermissions);
            await AssertProblemAsync(v2WithoutStore, HttpStatusCode.ServiceUnavailable, "etl.provider-unavailable");
        }

        await using var legacy = await EtlDesignApiTestHost.StartRuntimeAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Scoped<IEtlRuntimeOperations, LegacyRuntimeOperations>()));
        using var unavailable = await legacy.SendAsync(HttpMethod.Post, "/etl/runs/legacy/complete", permissions: CompletePermissions);
        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "etl.provider-unavailable");
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
