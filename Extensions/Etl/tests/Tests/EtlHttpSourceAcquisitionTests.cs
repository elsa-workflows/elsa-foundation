using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Source_acquisition_fetches_real_pages_and_retains_empty_page_continuation()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, request.RawTarget.Contains("cursor=")
                ? "{\"records\":[{\"id\":1,\"name\":\"Ada\"}]}" : "{\"records\":[],\"next\":\"a+b &c\"}")));
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server);
        var lease = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker"));

        var empty = await fixture.AcquireAsync(lease);
        Assert.Null(empty.FailureCode);
        Assert.Equal(0, empty.Page!.Descriptor.RecordCount);
        Assert.Equal("a+b &c", empty.Page.Descriptor.NextContinuation);
        var advanced = await fixture.Store.AdvanceEmptyHttpPageAsync(empty.Lease!);
        Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
        var next = await fixture.AcquireAsync(advanced.Lease!);

        Assert.Null(next.FailureCode);
        Assert.Single(next.Page!.DecodedPage.Records);
        Assert.Equal(1, next.Page.Descriptor.PageSequence);
        Assert.Null(next.Page.Descriptor.NextContinuation);
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains("cursor=a%2Bb%20%26c", server.Requests[1].RawTarget, StringComparison.Ordinal);
        var replay = await fixture.AcquireAsync(next.Lease!);
        Assert.Equal(next.Page.Descriptor, replay.Page!.Descriptor);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(2, await fixture.Context.ConnectorHttpPages.CountAsync());
        Assert.Equal(0, (await fixture.Context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);
    }

    [Fact]
    public async Task Source_acquisition_normalizes_relative_next_link_and_reopens_original_page_bytes()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, request.RawTarget.Contains("page=2")
                ? "{\"records\":[{\"id\":1,\"name\":\"Ada\"}]}" : "{\"records\":[],\"next\":\"?page=2\"}")));
        var pipeline = EtlV2TestPipelines.Create();
        pipeline = pipeline with { Source = ((EtlHttpJsonSource)pipeline.Source) with { Pagination = new EtlNextLinkPagination("/next") } };
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server, pipeline: pipeline);
        var lease = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker"));

        var first = await fixture.AcquireAsync(lease);
        Assert.Null(first.FailureCode);
        Assert.Equal(server.CreateUri("/a?page=2").AbsoluteUri, first.Page!.Descriptor.NextContinuation);
        var reopened = await fixture.AcquireAsync(first.Lease!);
        Assert.Equal(first.Page.Descriptor, reopened.Page!.Descriptor);
        Assert.Contains("?page=2", System.Text.Encoding.UTF8.GetString(reopened.Page.RawBody.Span), StringComparison.Ordinal);
        var advanced = await fixture.Store.AdvanceEmptyHttpPageAsync(reopened.Lease!);
        var second = await fixture.AcquireAsync(advanced.Lease!);

        Assert.Null(second.FailureCode);
        Assert.Single(second.Page!.DecodedPage.Records);
        Assert.Null(second.Page.Descriptor.NextContinuation);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("/a?page=2", server.Requests[1].RawTarget);
    }

    [Fact]
    public async Task Source_acquisition_recovers_manifest_published_before_SQL_capture_without_another_GET()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{\"records\":[]}")));
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server);
        var original = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "original"));
        var begin = await fixture.Store.BeginConnectorHttpSourceAsync(original, Guid.NewGuid().ToString("N"));
        var position = Assert.IsType<EtlConnectorHttpPosition>(original.Position);
        await using (var binding = await fixture.ResolveAsync())
        {
            var requests = new EtlHttpRequestBuilder((EtlHttpJsonSource)fixture.Pipeline.Source, binding, fixture.Pipeline.Http);
            var response = await fixture.Transport.FetchAsync(binding, requests.RequestUri(position.Request), begin.Permit!, 1024);
            var cache = new EtlHttpPageCache(fixture.Root, fixture.Admission, 0, requests);
            await cache.PublishAsync(0, position.Request, Guid.ParseExact(begin.Permit!.RequestId, "N"),
                original.Fence, response.ResponseReceivedAtUtc!.Value, response.Body!.Value);
        }
        Assert.Empty(await fixture.Context.ConnectorHttpPages.ToListAsync());
        Assert.Equal(EtlStoreStatus.Applied, await fixture.Store.ReleaseConnectorPartitionAsync(original));

        // A new context/store represents a successor, not the original EF tracker.
        await using var replacementContext = fixture.Database.CreateContext();
        var replacementStore = fixture.Database.CreateStore(replacementContext);
        var replacement = Assert.IsType<EtlConnectorPartitionLease>(await replacementStore.ClaimConnectorPartitionAsync(
            original.Scope, original.RunId, "replacement"));
        var recovered = await fixture.AcquireAsync(replacement, replacementStore);

        Assert.Null(recovered.FailureCode);
        Assert.NotNull(recovered.Page);
        Assert.Single(server.Requests);
        var retained = await replacementContext.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        var fetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(retained.FetchJson!);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured, fetch.Phase);
        Assert.Equal(original.Fence, fetch.Capture!.Fence);
        Assert.Equal(begin.Permit!.RequestId, fetch.Capture.RequestId);
        Assert.Single(fetch.Attempts);
        Assert.Single(await replacementContext.ConnectorHttpPages.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_acquisition_never_refetches_missing_or_corrupt_attached_body(bool missing)
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{\"records\":[]}")));
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server);
        var original = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker"));
        var first = await fixture.AcquireAsync(original);
        Assert.Null(first.FailureCode);
        var path = Path.Combine(fixture.Root, first.Page!.Descriptor.Body.Key);
        if (missing) File.Delete(path);
        else await File.WriteAllTextAsync(path, "{\"records\":[42]}");

        var refused = await fixture.AcquireAsync(first.Lease!);

        Assert.Equal("etl.http.page-cache-corrupt", refused.FailureCode);
        Assert.Null(refused.Page);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Source_acquisition_persists_delay_and_another_shard_can_progress()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(request.RawTarget.StartsWith("/a", StringComparison.Ordinal)
                ? new EtlHttpLoopbackResponse(429, "", headers: new Dictionary<string, string> { ["Retry-After"] = "120" })
                : new EtlHttpLoopbackResponse(200, "{\"records\":[]}")));
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server, ["a", "b"]);
        var leases = await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker");

        var waiting = await fixture.AcquireAsync(leases[0]);
        Assert.Null(waiting.FailureCode);
        Assert.Null(waiting.Page);
        Assert.Equal(EtlStoreStatus.Applied, await fixture.Store.ReleaseConnectorPartitionAsync(leases[0]));
        var second = await fixture.AcquireAsync(leases[1]);
        Assert.Null(second.FailureCode);
        Assert.NotNull(second.Page);
        Assert.Null(await fixture.Store.ClaimConnectorPartitionAsync(leases[0].Scope, leases[0].RunId, "other"));
        var delayed = await fixture.Context.ConnectorHttpRequests.AsNoTracking().SingleAsync(x => x.PartitionOrdinal == 0);
        var fetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(delayed.FetchJson!);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, fetch.Phase);
        Assert.True(fetch.NotBefore > fetch.UpdatedAt.AddSeconds(100));
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task Source_acquisition_records_invalid_JSON_as_a_deterministic_failure()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "not-json-secret-value")));
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server);
        var lease = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker"));

        var failure = await fixture.AcquireAsync(lease);

        Assert.Equal("etl.http.page-invalid", failure.FailureCode);
        Assert.DoesNotContain("secret-value", failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.Page);
        var retained = await fixture.Context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        var fetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(retained.FetchJson!);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.InvalidPage, fetch.Failure);
        Assert.NotNull(Assert.Single(fetch.Attempts).Outcome!.ResponseReceivedAtUtc);
        Assert.Empty(await fixture.Context.ConnectorHttpPages.ToListAsync());
        var repeated = await fixture.AcquireAsync(lease);
        Assert.Equal("etl.http.page-invalid", repeated.FailureCode);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Source_acquisition_records_disallowed_next_link_without_sending_to_it()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{\"records\":[],\"next\":\"/not-approved\"}")));
        var pipeline = EtlV2TestPipelines.Create();
        pipeline = pipeline with { Source = ((EtlHttpJsonSource)pipeline.Source) with { Pagination = new EtlNextLinkPagination("/next") } };
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server, pipeline: pipeline);
        var lease = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "worker"));

        var failure = await fixture.AcquireAsync(lease);

        Assert.Equal("etl.http-endpoint-unavailable", failure.FailureCode);
        var retained = await fixture.Context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        var fetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(retained.FetchJson!);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.InvalidPage, fetch.Failure);
        Assert.NotNull(Assert.Single(fetch.Attempts).Outcome!.ResponseReceivedAtUtc);
        Assert.Empty(await fixture.Context.ConnectorHttpPages.ToListAsync());
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Source_acquisition_late_owner_leaves_published_page_for_current_owner_without_refetch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await EtlHttpLoopbackServer.StartAsync(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new EtlHttpLoopbackResponse(200, "{\"records\":[]}");
        });
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(server);
        var original = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "original"));
        var inFlight = fixture.AcquireAsync(original).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var successorContext = fixture.Database.CreateContext();
            var successorStore = fixture.Database.CreateStore(successorContext);
            Assert.Equal(EtlStoreStatus.Applied, await successorStore.ReleaseConnectorPartitionAsync(original));
            var successor = Assert.IsType<EtlConnectorPartitionLease>(await successorStore.ClaimConnectorPartitionAsync(
                original.Scope, original.RunId, "successor"));
            release.TrySetResult();
            var stale = await inFlight;
            Assert.Equal(EtlStoreStatus.LeaseLost, stale.Status);
            Assert.Null(stale.Page);

            var recovered = await fixture.AcquireAsync(successor, successorStore);

            Assert.Null(recovered.FailureCode);
            Assert.Equal(original.Fence, recovered.Page!.Provenance.Fence);
            Assert.Single(server.Requests);
            Assert.Single(await successorContext.ConnectorHttpPages.AsNoTracking().ToListAsync());
        }
        finally
        {
            release.TrySetResult();
            await inFlight;
        }
    }

    private sealed class HttpAcquisitionFixture : IAsyncDisposable
    {
        public required TestDatabase Database { get; init; }
        public required EtlRuntimeSqliteDbContext Context { get; init; }
        public required EfEtlRuntimeStore Store { get; init; }
        public required HostHttpBindings Bindings { get; init; }
        public required EtlPipelineV2 Pipeline { get; init; }
        public required EtlConnectorRunAdmission Admission { get; init; }
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "etl-source-acquisition-" + Guid.NewGuid().ToString("N"));
        public EtlHttpTransport Transport { get; } = new(TimeProvider.System);

        public static async Task<HttpAcquisitionFixture> CreateAsync(EtlHttpLoopbackServer server, string[]? shards = null,
            EtlPipelineV2? pipeline = null)
        {
            shards ??= ["a"];
            var scope = new EtlScope("tenant", "source-acquisition");
            var options = new EtlHostHttpBindingOptions
            {
                TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "source",
                Role = "Source", Revision = "v1", Endpoint = server.CreateUri("/" + shards[0]).AbsoluteUri, AllowHttp = true,
                Shards = shards.Select(id => new EtlHostHttpShardOptions { Id = id, SeedUri = server.CreateUri("/" + id).AbsoluteUri }).ToList()
            };
            var bindings = new HostHttpBindings([options]);
            await using var binding = await bindings.ResolveCurrentAsync(scope, "source", "Source");
            pipeline ??= EtlV2TestPipelines.Create();
            pipeline = pipeline with { Http = pipeline.Http with { MaxPageBytes = 1024, MaxContinuationBytes = 64 } };
            var admission = ConnectorAdmission("source-acquisition", scope, pipeline, shards);
            admission = admission with { Bindings = admission.Bindings with
            { Source = new(EtlConnectorKind.Http, new("source", "v1", binding.Handle, null)) } };
            var database = await TestDatabase.CreateAsync();
            var context = database.CreateContext();
            var fixture = new HttpAcquisitionFixture { Database = database, Context = context, Store = database.CreateStore(context),
                Bindings = bindings, Pipeline = pipeline, Admission = admission };
            Directory.CreateDirectory(fixture.Root);
            return fixture;
        }

        public ValueTask<HostHttpResolvedBinding> ResolveAsync() => Bindings.ResolveExactAsync(Admission.Invocation.Scope,
            "source", "Source", "v1", Admission.Bindings.Source.Revision.Handle);

        public ValueTask<EtlHttpSourceAcquisition> AcquireAsync(EtlConnectorPartitionLease lease, IEtlConnectorRuntimeStore? store = null) =>
            new EtlHttpSourceAcquirer(store ?? Store, Bindings, Transport, Root, TimeProvider.System)
                .AcquireAsync(Admission, lease);

        public async ValueTask DisposeAsync()
        {
            Transport.Dispose();
            await Context.DisposeAsync();
            await Database.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
