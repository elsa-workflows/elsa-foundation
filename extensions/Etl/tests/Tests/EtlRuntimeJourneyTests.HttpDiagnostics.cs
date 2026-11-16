using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Http_worker_diagnostics_and_error_route_omit_values_observed_on_the_wire()
    {
        const string queryKey = "synthetic-worker-query-key";
        const string cursor = "opaque-worker-cursor";
        const string rejectedValue = "private-invalid-row-value";
        const string responseValue = "private-worker-response";
        using var diagnostics = new EtlDiagnosticCapture();
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(request.RawTarget.Contains("cursor=", StringComparison.Ordinal)
                ? new EtlHttpLoopbackResponse(400, responseValue, new Dictionary<string, string> { ["X-Private"] = responseValue })
                : new EtlHttpLoopbackResponse(200, JsonSerializer.Serialize(new
                {
                    records = new object[] { new { id = 1, name = "Ada" }, new { id = rejectedValue, name = "rejected" } },
                    next = cursor
                }))));
        var seed = endpoint.CreateUri("/records?api_key=" + queryKey);
        string[] markers = [queryKey, cursor, rejectedValue, responseValue, endpoint.CreateUri("/records").AbsoluteUri];
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with { Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")) };
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            resultDelivery: new DiagnosticFailureDelivery(string.Join(" ", markers)),
            configureServices: services => services.AddLogging(logging => logging
                .AddFilter<EtlDiagnosticCapture>(null, LogLevel.Trace).AddProvider(diagnostics)),
            configureRuntime: runtime =>
            {
                runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
                runtime.HttpBindings.Add(HttpBinding(files.Scope, endpoint, "source", "Source", "/records",
                    shards: [new() { Id = "one", SeedUri = seed.AbsoluteUri }]));
            });
        Assert.True(host.Services.GetRequiredService<ILogger<EtlRuntimeWorker>>().IsEnabled(LogLevel.Trace));
        var invocation = ConnectorInvocation(files.Scope, "http-diagnostic-boundary", pipeline);
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        await diagnostics.WorkerWarning.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.StopAsync(stop.Token);

        Assert.Equal("etl.http.source-response-rejected", run.TerminalResult!.Diagnostic!.Code);
        Assert.Equal(1, run.AcceptedRecords);
        Assert.Equal(1, run.RejectedRecords);
        var errors = await ReadConnectorRouteAsync(endpoint, files.ErrorRoot, "/errors", false);
        var error = Assert.Single(errors);
        Assert.Equal("etl.invalid-value", error.GetProperty("code").GetString());
        EtlDiagnosticCapture.AssertAbsent(error.GetRawText() + JsonSerializer.Serialize(run.TerminalResult), markers);
        Assert.Equal(2, endpoint.Requests.Count);
        Assert.Contains(queryKey, endpoint.Requests[0].RawTarget, StringComparison.Ordinal);
        Assert.Contains(cursor, endpoint.Requests[1].RawTarget, StringComparison.Ordinal);
        Assert.Contains(diagnostics.Logs, log => log.Message ==
            "ETL attempt stopped after InvalidOperationException; durable work remains recoverable.");
        Assert.All(diagnostics.Logs, log =>
        {
            EtlDiagnosticCapture.AssertAbsent(log.Message + log.Details + log.Exception, markers);
            Assert.Null(log.Exception);
        });
        Assert.DoesNotContain(diagnostics.Activities, item => item.Source.StartsWith("Elsa.Etl", StringComparison.Ordinal));
        // Platform spans have their own host-controlled boundary, unlike ETL diagnostics above.
        diagnostics.AssertPlatformRequests(2, endpoint.CreateUri("/records").AbsoluteUri,
            queryKey, cursor, rejectedValue, responseValue);
    }

    private sealed class DiagnosticFailureDelivery(string message) : IEtlResultDelivery
    {
        public ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message, new Exception(message));
    }
}
