using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Ingestion;

public sealed class OtlpIngestionEndpointAuthenticationTests
{
    [Fact]
    public async Task CustomAuthenticatedClaimsReachContributorAfterRedaction()
    {
        var contributor = new RecordingContributor();
        var ingestor = new OpenTelemetryIngestor(new OpenTelemetryRedactor(Options.Create(new OpenTelemetryDiagnosticsOptions())), new NullStore(), new NullLiveFeed(), [contributor]);
        var authenticated = OpenTelemetryIngestionContext.Authenticated("collector-source-42", new Dictionary<string, string> { ["workspace"] = "workspace-1" });
        var context = CreateContext(LogsPayloadWithSecret(), "source-token");
        await new OtlpHttpIngestionHandler(ingestor, new PerSourceTokenAuthenticator("source-token", authenticated), Options.Create(new OpenTelemetryDiagnosticsOptions())).HandleAsync(context, OtlpSignal.Logs);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal("workspace-1", Assert.Single(contributor.Contexts).Claims["workspace"]);
        Assert.Equal("[Redacted]", Assert.Single(Assert.Single(contributor.Batches).Logs).Attributes["password"]);
    }

    [Fact]
    public async Task RejectedAuthenticationNeverIngests()
    {
        var ingestor = new RecordingIngestor();
        var body = new ThrowingReadStream();
        var context = CreateContext(body, "wrong-token");
        await new OtlpHttpIngestionHandler(ingestor, new PerSourceTokenAuthenticator("source-token", OpenTelemetryIngestionContext.Authenticated("source-1")), Options.Create(new OpenTelemetryDiagnosticsOptions())).HandleAsync(context, OtlpSignal.Logs);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
        Assert.Equal(0, body.ReadCount);
    }

    private static DefaultHttpContext CreateContext(byte[] body, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-source-token"] = token;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext CreateContext(Stream body, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-source-token"] = token;
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static byte[] LogsPayloadWithSecret()
    {
        var resource = Message(1, KeyValue("service.name", "elsa-server"));
        var log = Join(Message(5, AnyString("boom")), Message(6, KeyValue("password", "secret-value")));
        return Message(1, Join(Message(1, resource), Message(2, Message(2, log))));
    }
    private static byte[] KeyValue(string key, string value) => Join(String(1, key), Message(2, AnyString(value)));
    private static byte[] AnyString(string value) => String(1, value);
    private static byte[] Message(int fieldNumber, byte[] value) => Join(Varint((ulong)((fieldNumber << 3) | 2)), Varint((ulong)value.Length), value);
    private static byte[] String(int fieldNumber, string value) => Message(fieldNumber, Encoding.UTF8.GetBytes(value));
    private static byte[] Join(params byte[][] segments) => segments.SelectMany(segment => segment).ToArray();
    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        { bytes.Add((byte)(value | 0x80)); value >>= 7; }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private sealed class PerSourceTokenAuthenticator(string expectedToken, OpenTelemetryIngestionContext authenticatedContext) : IOtlpRequestAuthenticator
    {
        public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(HttpContext httpContext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(httpContext.Request.Headers.TryGetValue("x-source-token", out var token) && token == expectedToken ? OtlpRequestAuthenticationResult.Accept(authenticatedContext) : OtlpRequestAuthenticationResult.Rejected);
    }

    private sealed class ThrowingReadStream : Stream
    {
        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) { ReadCount++; throw new InvalidOperationException("Rejected authentication must not read the body."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { ReadCount++; throw new InvalidOperationException("Rejected authentication must not read the body."); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingContributor : IOpenTelemetryIngestionContributor
    {
        public List<OpenTelemetryBatch> Batches { get; } = [];
        public List<OpenTelemetryIngestionContext> Contexts { get; } = [];
        public ValueTask ContributeAsync(OpenTelemetryBatch batch, OpenTelemetryIngestionContext context, CancellationToken cancellationToken = default) { Batches.Add(batch); Contexts.Add(context); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingIngestor : IOpenTelemetryIngestor
    {
        public int IngestCount { get; private set; }
        public ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) { IngestCount++; return ValueTask.CompletedTask; }
        public ValueTask IngestAsync(OpenTelemetryBatch batch, OpenTelemetryIngestionContext context, CancellationToken cancellationToken = default) { IngestCount++; return ValueTask.CompletedTask; }
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
        public async IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(OpenTelemetryTraceFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
    }
}
