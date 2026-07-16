using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Elsa.Diagnostics.OpenTelemetry.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Ingestion;

public sealed class OtlpIngestionEndpointAuthenticationTests
{
    [Fact]
    public async Task CustomAuthenticatedClaimsReachContributorAfterRedaction()
    {
        var contributor = new RecordingContributor();
        var ingestor = new OpenTelemetryIngestor(
            new OpenTelemetryRedactor(Options.Create(new OpenTelemetryDiagnosticsOptions())),
            new NullStore(),
            new NullLiveFeed(),
            [contributor]);
        var context = OpenTelemetryIngestionContext.Authenticated(
            "collector-source-42",
            new Dictionary<string, string>
            {
                ["workspace"] = "workspace-1",
                ["application"] = "app-1",
                ["environment"] = "production"
            });
        var endpoint = CreateLogsEndpoint(
            ingestor,
            new PerSourceTokenAuthenticator("source-token", context),
            LogsPayloadWithSecret(),
            "source-token");

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("workspace-1", Assert.Single(contributor.Contexts).Claims["workspace"]);
        Assert.Equal("[Redacted]", Assert.Single(Assert.Single(contributor.Batches).Logs).Attributes["password"]);
    }

    [Fact]
    public async Task RejectedAuthenticationNeverIngests()
    {
        var ingestor = new RecordingIngestor();
        var endpoint = CreateLogsEndpoint(
            ingestor,
            new PerSourceTokenAuthenticator("source-token", OpenTelemetryIngestionContext.Authenticated("source-1")),
            []);

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status401Unauthorized, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
    }

    private static BaseEndpoint CreateLogsEndpoint(
        IOpenTelemetryIngestor ingestor,
        IOtlpRequestAuthenticator authenticator,
        byte[] body,
        string? sourceToken = null)
    {
        var endpointType = typeof(OpenTelemetryFeature).Assembly
            .GetType("Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion.LogsIngestionEndpoint", throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var options = Options.Create(new OpenTelemetryDiagnosticsOptions());
        Action<DefaultHttpContext> configure = context =>
        {
            if (sourceToken != null)
                context.Request.Headers["x-source-token"] = sourceToken;
            context.Request.Body = new MemoryStream(body);
            context.Response.Body = new MemoryStream();
        };

        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { ingestor, authenticator, options }])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint)
    {
        var handle = endpoint.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "HandleAsync"
                              && method.DeclaringType?.Name == "OtlpIngestionEndpointBase"
                              && method.GetParameters() is [var cancellation]
                              && cancellation.ParameterType == typeof(CancellationToken));
        return (Task)handle.Invoke(endpoint, [CancellationToken.None])!;
    }

    private static byte[] LogsPayloadWithSecret()
    {
        var resource = Message(1, KeyValue("service.name", "elsa-server"));
        var log = Join(
            Message(5, AnyString("boom")),
            Message(6, KeyValue("password", Guid.NewGuid().ToString("N"))));
        return Message(1, Join(Message(1, resource), Message(2, Message(2, log))));
    }

    private static byte[] KeyValue(string key, string value) => Join(String(1, key), Message(2, AnyString(value)));
    private static byte[] AnyString(string value) => String(1, value);
    private static byte[] Message(int fieldNumber, byte[] value) => Join(Varint((ulong)((fieldNumber << 3) | 2)), Varint((ulong)value.Length), value);
    private static byte[] String(int fieldNumber, string value) => Message(fieldNumber, Encoding.UTF8.GetBytes(value));

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static byte[] Join(params byte[][] segments) => segments.SelectMany(segment => segment).ToArray();

    private sealed class PerSourceTokenAuthenticator(
        string expectedToken,
        OpenTelemetryIngestionContext authenticatedContext) : IOtlpRequestAuthenticator
    {
        public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            var result = httpContext.Request.Headers.TryGetValue("x-source-token", out var token) && token == expectedToken
                ? OtlpRequestAuthenticationResult.Accept(authenticatedContext)
                : OtlpRequestAuthenticationResult.Rejected;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingContributor : IOpenTelemetryIngestionContributor
    {
        public List<OpenTelemetryBatch> Batches { get; } = [];
        public List<OpenTelemetryIngestionContext> Contexts { get; } = [];

        public ValueTask ContributeAsync(OpenTelemetryBatch redactedBatch, OpenTelemetryIngestionContext ingestionContext, CancellationToken cancellationToken = default)
        {
            Batches.Add(redactedBatch);
            Contexts.Add(ingestionContext);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingIngestor : IOpenTelemetryIngestor
    {
        public int IngestCount { get; private set; }

        public ValueTask IngestAsync(OpenTelemetryBatch batch, OpenTelemetryIngestionContext ingestionContext, CancellationToken cancellationToken = default)
        {
            IngestCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullStore : IOpenTelemetryStore
    {
        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryResourceResult([], 0));
        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryTraceResult([], 0));
        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) => ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);
        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryMetricResult([], [], 0));
        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryLogResult([], 0));
        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryStorageDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    private sealed class NullLiveFeed : IOpenTelemetryLiveFeed
    {
        public ValueTask PublishAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(
            OpenTelemetryTraceFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
