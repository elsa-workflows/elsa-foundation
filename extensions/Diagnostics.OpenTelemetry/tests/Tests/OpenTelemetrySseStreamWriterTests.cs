using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetrySseStreamWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Completed_stream_writes_real_frames_and_flushes_each_event()
    {
        var response = new DefaultHttpContext().Response;
        var body = new TrackingStream();
        response.Body = body;
        var writer = new OpenTelemetrySseStreamWriter(new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer()));

        await writer.StreamAsync(response, CompletedItems(), CancellationToken.None);

        var text = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal(2, body.FlushCount);
        Assert.Contains("event: resource\ndata:", text, StringComparison.Ordinal);
        Assert.Contains("event: trace\ndata:", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_drains_pending_move_and_disposes_the_async_enumerator()
    {
        var response = new DefaultHttpContext().Response;
        response.Body = new TrackingStream();
        var writer = new OpenTelemetrySseStreamWriter(new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer()));
        var disposed = false;
        using var cancellation = new CancellationTokenSource();

        var running = writer.StreamAsync(response, PendingItems(() => disposed = true), cancellation.Token);
        await Task.Delay(20);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.True(disposed);
    }

    private static async IAsyncEnumerable<OpenTelemetryStreamItem> CompletedItems()
    {
        yield return new OpenTelemetryStreamItem
        {
            Resource = new TelemetryResource("resource-1", "service-1", null, "dotnet", new Dictionary<string, string?>(), Now, TelemetryResourceStatus.Active)
        };
        yield return new OpenTelemetryStreamItem
        {
            Trace = new TelemetryTrace("trace-1", "root-1", "Root", Now, Now.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, ["resource-1"], [], 1)
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<OpenTelemetryStreamItem> PendingItems(Action disposed, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            yield return new OpenTelemetryStreamItem
            {
                Resource = new TelemetryResource("resource-1", "service-1", null, "dotnet", new Dictionary<string, string?>(), Now, TelemetryResourceStatus.Active)
            };
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            disposed();
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public int FlushCount { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken) { FlushCount++; return Task.CompletedTask; }
    }
}
