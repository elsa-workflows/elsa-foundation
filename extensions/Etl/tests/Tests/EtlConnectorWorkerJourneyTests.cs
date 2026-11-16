using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task Shared_worker_executes_connector_routes_lookup_and_paging(bool csvSource, bool httpMain, bool httpErrors)
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,skip\n99,unknown\n3,Bram\n");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(request.Method == "POST" ? 204 : 200,
                request.Method == "POST" ? "" : request.RawTarget switch
                {
                    "/a" => "{\"records\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"skip\"}],\"next\":\"done\"}",
                    "/a?cursor=done" => "{\"records\":[],\"next\":null}",
                    "/b" => "{\"records\":[{\"id\":99,\"name\":\"unknown\"},{\"id\":3,\"name\":\"Bram\"}],\"next\":null}",
                    _ => throw new InvalidOperationException("Unexpected fixture request.")
                })));
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with
        {
            Source = csvSource ? new EtlCsvSource("source", EtlTestPipelines.Create().SourceSchema) : baseline.Source,
            Lookup = new("lookup", EtlTestPipelines.Create().LookupSchema, "id", "id"),
            Routes = new(httpMain ? new EtlHttpDestination("output") : new EtlJsonlDestination("output"),
                httpErrors ? new EtlHttpDestination("errors") : new EtlJsonlDestination("errors")),
            Mapping = [new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")],
            Filters = [new("name", EtlComparison.NotEqual, JsonSerializer.SerializeToElement("skip"))],
            Limits = baseline.Limits with { MaxBatchRecords = 1 }
        };
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, resultDelivery: delivery,
            configureRuntime: runtime => ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource, httpMain, httpErrors));
        var invocation = ConnectorInvocation(files.Scope, "connector-worker", pipeline);
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.rows-rejected", run.TerminalResult!.Diagnostic!.Code);
        Assert.Equal(2, run.AcceptedRecords);
        Assert.Equal(1, run.RejectedRecords);
        Assert.Equal(1, run.FilteredRecords);
        Assert.Equal(0, run.OutstandingRecords);
        Assert.Equal(0, run.OutstandingBytes);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(invocation.RunId, (await delivery.WaitForDeliveryAsync(timeout.Token)).RunId);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<IEtlArtifactCompiler>());
        var retained = Assert.IsType<EtlConnectorRunView>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId));
        Assert.NotNull(retained.Prepared!.Lookup);
        Assert.Equal(csvSource, retained.Prepared.File is not null);
        var main = await ReadConnectorRouteAsync(endpoint, files.OutputRoot, "/output", httpMain);
        Assert.Equal(new[] { "Ada", "Bram" }, main.Select(item => item.GetProperty("name").GetString()).Order().ToArray());
        Assert.Equal(new[] { "one", "three" }, main.Select(item => item.GetProperty("category").GetString()).Order().ToArray());
        Assert.Single(await ReadConnectorRouteAsync(endpoint, files.ErrorRoot, "/errors", httpErrors));
        var posts = endpoint.Requests.Where(request => request.Method == "POST").ToArray();
        Assert.All(posts, request => Assert.False(string.IsNullOrWhiteSpace(request.Headers["Idempotency-Key"])));
        Assert.Equal(posts.Length, posts.Select(request => request.Headers["Idempotency-Key"]).Distinct().Count());
        Assert.Equal(csvSource ? 0 : 3, endpoint.Requests.Count(request => request.Method == "GET"));
    }

    [Fact]
    public async Task Connector_capability_remains_optional_for_a_file_only_provider()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.RemoveAll<IEtlConnectorRuntimeStore>());
        var connector = ConnectorInvocation(files.Scope, "unsupported-connector");
        await files.AdmitAsync(host, connector, EtlStoreStatus.Replay);
        var refusal = await files.WaitForTerminalAsync(host, connector.RunId);
        Assert.Equal("etl.connector-runtime-unavailable", refusal.TerminalResult!.Diagnostic!.Code);
        var file = files.Invocation(EtlTestPipelines.Create(), "file-still-runs");
        await files.AdmitAsync(host, file);
        Assert.Equal(EtlRunStatus.Succeeded, (await files.WaitForTerminalAsync(host, file.RunId)).Status);
    }

    [Fact]
    public async Task Http_only_worker_yields_a_retry_slot_and_runs_another_shard_before_retrying()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var attempts = new System.Collections.Concurrent.ConcurrentQueue<(string Path, DateTimeOffset At)>();
        var first = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
        {
            attempts.Enqueue((request.RawTarget, DateTimeOffset.UtcNow));
            if (request.Method == "POST") return ValueTask.FromResult(new EtlHttpLoopbackResponse(204, ""));
            if (request.RawTarget == "/a" && Interlocked.Increment(ref first) == 1)
                return ValueTask.FromResult(new EtlHttpLoopbackResponse(429, "", new Dictionary<string, string> { ["Retry-After"] = "3" }));
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(200,
                "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}"));
        });
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false,
            configureRuntime: runtime =>
            {
                runtime.MaxConcurrentAttempts = 1;
                ConfigureConnectorBindings(runtime, files.Scope, endpoint, false, true, true);
            });
        var invocation = ConnectorInvocation(files.Scope, "http-only-retry");
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.True(run.Status == EtlRunStatus.Succeeded, run.TerminalResult?.Diagnostic?.Code);
        Assert.Equal(2, run.AcceptedRecords);
        var requests = attempts.ToArray();
        var a = requests.Where(request => request.Path == "/a").ToArray();
        Assert.Equal(2, a.Length);
        var b = Assert.Single(requests, request => request.Path == "/b");
        Assert.True(b.At < a[1].At, "Delayed source work must release the only physical slot.");
        Assert.True(a[1].At - a[0].At >= TimeSpan.FromSeconds(3), "Retry-After must not be shortened.");
    }

    [Fact]
    public async Task Fully_filtered_connector_batch_advances_without_dispatching_an_output()
    {
        await using var files = new RuntimeFiles("id,name\n");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200,
                "{\"records\":[{\"id\":1,\"name\":\"skip\"}],\"next\":null}")));
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false,
            configureRuntime: runtime => ConfigureConnectorBindings(runtime, files.Scope, endpoint, false, true, true));
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Filters = [new("name", EtlComparison.NotEqual, JsonSerializer.SerializeToElement("skip"))]
        };
        var invocation = ConnectorInvocation(files.Scope, "filtered-connector", pipeline);
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.True(run.Status == EtlRunStatus.Succeeded, run.TerminalResult?.Diagnostic?.Code);
        Assert.Equal(2, run.FilteredRecords);
        Assert.Equal(0, run.AcceptedRecords);
        Assert.Equal(0, run.RejectedRecords);
        Assert.Equal(0, run.OutstandingRecords);
        Assert.Equal(0, run.OutstandingBytes);
        Assert.Equal(2, endpoint.Requests.Count);
        Assert.All(endpoint.Requests, request => Assert.Equal("GET", request.Method));
    }

    [Fact]
    public async Task Worker_settles_irreducible_output_retention_limit_before_any_post()
    {
        await using var files = new RuntimeFiles("id,name\n");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200,
                "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}")));
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false,
            configureRuntime: runtime => ConfigureConnectorBindings(runtime, files.Scope, endpoint, false, true, true));
        var baseline = EtlV2TestPipelines.Create();
        var invocation = ConnectorInvocation(files.Scope, "output-retention-limit", baseline with
        {
            Limits = baseline.Limits with { MaxRetainedOutputBytes = 1 }
        });
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.connector-output-retention-limit", run.TerminalResult!.Diagnostic!.Code);
        Assert.Equal(0, run.AcceptedRecords);
        // Failure keeps unaccepted responsibility as evidence; it must never fabricate acceptance
        // or discard the descriptor merely to clear the outstanding counters.
        Assert.InRange(run.OutstandingRecords, 1, 2);
        Assert.True(run.OutstandingBytes > 0);
        await using var operation = host.Services.CreateAsyncScope();
        Assert.Null(await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .ClaimConnectorPartitionAsync(files.Scope, invocation.RunId, "after-terminal"));
        Assert.All(endpoint.Requests, request => Assert.Equal("GET", request.Method));
    }

    private static void ConfigureConnectorBindings(EtlRuntimeFeature runtime, EtlScope scope, EtlHttpLoopbackServer endpoint,
        bool csvSource, bool httpMain, bool httpErrors)
    {
        if (!csvSource)
        {
            runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
            runtime.HttpBindings.Add(HttpBinding(scope, endpoint, "source", "Source", "/a", ["/a", "/b"],
                [new() { Id = "a", SeedUri = endpoint.CreateUri("/a").AbsoluteUri },
                 new() { Id = "b", SeedUri = endpoint.CreateUri("/b").AbsoluteUri }]));
        }
        if (httpMain)
        {
            runtime.FileBindings.RemoveAll(binding => binding.Role == "MainOutput");
            runtime.HttpBindings.Add(HttpBinding(scope, endpoint, "output", "MainOutput", "/output"));
        }
        if (httpErrors)
        {
            runtime.FileBindings.RemoveAll(binding => binding.Role == "ErrorOutput");
            runtime.HttpBindings.Add(HttpBinding(scope, endpoint, "errors", "ErrorOutput", "/errors"));
        }
    }

    private static EtlHostHttpBindingOptions HttpBinding(EtlScope scope, EtlHttpLoopbackServer endpoint,
        string alias, string role, string path, IList<string>? allowedPaths = null, IList<EtlHostHttpShardOptions>? shards = null) => new()
    {
        TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = alias, Role = role,
        Revision = "v1", Endpoint = endpoint.CreateUri(path).AbsoluteUri, AllowHttp = true,
        AllowedPaths = allowedPaths ?? [path], Shards = shards ?? []
    };

    private static async Task<IReadOnlyList<JsonElement>> ReadConnectorRouteAsync(EtlHttpLoopbackServer endpoint,
        string directory, string path, bool http)
    {
        var result = new List<JsonElement>();
        if (http)
        {
            foreach (var request in endpoint.Requests.Where(request => request.Method == "POST" && request.RawTarget == path))
            {
                using var document = JsonDocument.Parse(request.Body);
                result.AddRange(document.RootElement.EnumerateArray().Select(value => value.Clone()));
            }
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
                foreach (var line in await File.ReadAllLinesAsync(file))
                {
                    using var document = JsonDocument.Parse(line);
                    result.Add(document.RootElement.Clone());
                }
        }
        return result;
    }
}
