using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryLogsEndpointTests
{
    [Fact]
    public void Endpoint_uses_the_authorized_logs_search_contract()
    {
        var endpoint = CreateEndpoint(new RecordingProvider(new([], 0)));

        Assert.Equal(Http.POST.ToString(), Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal("/diagnostics/opentelemetry/logs/search", Assert.Single(endpoint.Definition.Routes));
        Assert.NotEmpty(endpoint.Definition.PreBuiltUserPolicies!);
        Assert.All(
            endpoint.Definition.PreBuiltUserPolicies!,
            policy => Assert.Equal(ElsaEndpointPermissions.ComposePolicy(["Diagnostics:OpenTelemetry"]), policy));
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public async Task Endpoint_binds_the_filter_and_returns_the_provider_result()
    {
        var expected = new OpenTelemetryLogResult([], 7);
        var provider = new RecordingProvider(expected);
        var endpoint = CreateEndpoint(provider);
        var filter = new OpenTelemetryLogFilter
        {
            ResourceId = "resource-1",
            ServiceName = "orders",
            TraceId = "trace-1",
            SpanId = "span-1",
            Severity = "Error",
            Search = "failed",
            Take = 25
        };
        using var cancellation = new CancellationTokenSource();

        var execute = endpoint.GetType().GetMethod(
            "ExecuteAsync",
            [typeof(OpenTelemetryLogFilter), typeof(CancellationToken)])!;
        var result = await (Task<OpenTelemetryLogResult>)execute.Invoke(
            endpoint,
            [filter, cancellation.Token])!;

        Assert.Same(filter, provider.Filter);
        Assert.Equal(cancellation.Token, provider.CancellationToken);
        Assert.Same(expected, result);
    }

    private static BaseEndpoint CreateEndpoint(IOpenTelemetryProvider provider)
    {
        var endpointType = typeof(global::Elsa.Diagnostics.OpenTelemetry.OpenTelemetryFeature).Assembly.GetType(
            "Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetry.Logs.Endpoint",
            throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        var endpoint = (BaseEndpoint)create.Invoke(
            null,
            [(Action<DefaultHttpContext>)(_ => { }), new object[] { provider }])!;
        endpoint.Configure();
        return endpoint;
    }

    private sealed class RecordingProvider(OpenTelemetryLogResult result) : IOpenTelemetryProvider
    {
        public OpenTelemetryLogFilter? Filter { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<OpenTelemetryLogResult> GetLogsAsync(
            OpenTelemetryLogFilter filter,
            CancellationToken cancellationToken = default)
        {
            Filter = filter;
            CancellationToken = cancellationToken;
            return ValueTask.FromResult(result);
        }

        public ValueTask<OpenTelemetryResourceResult> GetResourcesAsync(
            OpenTelemetryResourceFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OpenTelemetryTraceResult> GetTracesAsync(
            OpenTelemetryTraceFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
            string traceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OpenTelemetryMetricResult> GetMetricsAsync(
            OpenTelemetryMetricFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OpenTelemetryStorageDiagnostics> GetStorageDiagnosticsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<CollectorConfiguration> GetCollectorConfigurationAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
