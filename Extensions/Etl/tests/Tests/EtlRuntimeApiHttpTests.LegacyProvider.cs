using System.Net;
using System.Text.Json;
using Elsa.Etl.Runtime.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeApiHttpTests
{
    [Fact]
    public async Task Legacy_runtime_operations_keep_old_routes_and_report_connector_inspection_unavailable()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Scoped<IEtlRuntimeOperations, LegacyRuntimeOperations>()));

        using var legacyRoute = await host.SendAsync(HttpMethod.Get, "/etl/runs?limit=20", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, legacyRoute.StatusCode);
        using var legacyPage = await ReadJsonAsync(legacyRoute);
        Assert.Empty(legacyPage.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, legacyPage.RootElement.GetProperty("nextCursor").ValueKind);

        using var denied = await host.SendAsync(HttpMethod.Get, "/etl/runs/legacy/connector-partitions");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var unsupported = await host.SendAsync(HttpMethod.Get, "/etl/runs/legacy/connector-partitions",
            permissions: ReadPermissions);
        await AssertProblemAsync(unsupported, HttpStatusCode.ServiceUnavailable, "etl.provider-unavailable");
        var problem = await unsupported.Content.ReadAsStringAsync();
        Assert.DoesNotContain(nameof(LegacyRuntimeOperations), problem, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IEtlRuntimeOperations.ListConnectorPartitionsAsync), problem, StringComparison.Ordinal);
    }

    // This deliberately implements only the four operations that predate connector inspection.
    private sealed class LegacyRuntimeOperations : IEtlRuntimeOperations
    {
        public LegacyRuntimeOperations() { }

        public Task<EtlRunPage<EtlRunSummaryView>> ListRunsAsync(EtlRunPageRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new EtlRunPage<EtlRunSummaryView>([], null));

        public Task<EtlRunDetailView> GetRunAsync(EtlRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new EtlRunDetailView(request.Id, "legacy-selection", "running", null,
                0, 0, 0, 0, 0, null, null, null));

        public Task<EtlRunPage<EtlRunPartitionView>> ListPartitionsAsync(EtlRunPartitionsRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new EtlRunPage<EtlRunPartitionView>([], null));

        public Task<EtlRunCancellationView> CancelAsync(EtlRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new EtlRunCancellationView(false,
                new EtlRunTerminalResultView("cancelled", 0, 0, 0, null, DateTimeOffset.UnixEpoch)));
    }
}
