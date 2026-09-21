using System.Diagnostics;
using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Tests.Support;

/// <summary>
/// Bounded HTTP reader for an SSE exchange. It records complete frames and response metadata, then stops
/// at an explicit evidence boundary instead of waiting for an intentionally infinite response to reach EOF.
/// </summary>
public static class StructuredLogsStreamReader
{
    public static async Task<StructuredLogsStreamObservation> CaptureAsync(
        HttpClient client,
        HttpRequestMessage request,
        StructuredLogsStreamReaderOptions options,
        CancellationToken cancellationToken = default,
        Func<HttpResponseMessage, Task>? onHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(header => header.Key.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(",", group.SelectMany(header => header.Value).Order(StringComparer.Ordinal)),
                StringComparer.Ordinal);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(linkedCancellation.Token);
        if (onHeaders is not null)
            await onHeaders(response);
        var stopwatch = Stopwatch.StartNew();
        using var bytes = new MemoryStream();
        var frames = new List<string>();
        var frameOffset = 0;
        var buffer = new byte[Math.Min(8 * 1024, Math.Max(1, options.MaxBytes))];
        var terminalState = "Completed";
        var firstByteElapsed = TimeSpan.Zero;
        var automaticallyBounded = false;
        var cancellationObserved = false;

        try
        {
            while (bytes.Length < options.MaxBytes)
            {
                var remaining = checked((int)Math.Min(buffer.Length, options.MaxBytes - bytes.Length));
                var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), linkedCancellation.Token);
                if (read == 0)
                    break;

                if (bytes.Length == 0)
                    firstByteElapsed = stopwatch.Elapsed;

                await bytes.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                ExtractCompleteFrames(bytes, frames, ref frameOffset);
                if (frames.Count >= options.MaxFrames)
                {
                    automaticallyBounded = true;
                    break;
                }
            }

            if (bytes.Length >= options.MaxBytes)
            {
                automaticallyBounded = true;
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            cancellationObserved = true;
            terminalState = "Cancelled";
        }
        catch (HttpRequestException) when (linkedCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            cancellationObserved = true;
            terminalState = "Cancelled";
        }
        finally
        {
            if (automaticallyBounded)
                terminalState = "Bounded";
            else if (!cancellationObserved && cancellationToken.IsCancellationRequested)
                terminalState = "Cancelled";
        }

        // A transport read may coalesce several flushed SSE frames. Keep the observation bounded at the
        // requested complete-frame boundary instead of accidentally recording every frame in that read.
        var boundedFrames = frames.Take(options.MaxFrames).ToArray();
        var rawText = automaticallyBounded && boundedFrames.Length == options.MaxFrames
            ? string.Concat(boundedFrames)
            : Encoding.UTF8.GetString(bytes.ToArray());

        return new StructuredLogsStreamObservation(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            new SortedDictionary<string, string>(headers, StringComparer.Ordinal),
            rawText,
            boundedFrames,
            terminalState,
            firstByteElapsed);
    }

    private static void ExtractCompleteFrames(MemoryStream bytes, ICollection<string> frames, ref int frameOffset)
    {
        var text = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
        var offset = frameOffset;
        while (true)
        {
            var end = text.IndexOf("\n\n", offset, StringComparison.Ordinal);
            if (end < 0)
            {
                frameOffset = offset;
                return;
            }

            frames.Add(text[offset..(end + 2)]);
            offset = end + 2;
        }
    }
}

public sealed record StructuredLogsStreamReaderOptions
{
    public StructuredLogsStreamReaderOptions(int maxFrames = 128, int maxBytes = 64 * 1024)
    {
        MaxFrames = maxFrames > 0 ? maxFrames : throw new ArgumentOutOfRangeException(nameof(maxFrames));
        MaxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    }

    public int MaxFrames { get; }

    public int MaxBytes { get; }
}

public sealed record StructuredLogsStreamObservation(
    int StatusCode,
    string ContentType,
    IReadOnlyDictionary<string, string> Headers,
    string RawText,
    IReadOnlyList<string> Frames,
    string TerminalState,
    TimeSpan FirstByteElapsed)
{
    public bool HasCompleteFrame => Frames.Count > 0;

    public string FrameText => string.Join("\n", Frames);

    /// <summary>Presence/validity/bound evidence for each emitted SSE id, without exposing cursor internals.</summary>
    public IReadOnlyList<StructuredLogsCursorEvidence> CursorEvidence => Frames
        .Select(frame => frame.Split('\n').FirstOrDefault(line => line.StartsWith("id: ", StringComparison.Ordinal)))
        .Where(line => line is not null)
        .Select(line => line![4..])
        .Select(value => new StructuredLogsCursorEvidence(
            Present: true,
            Valid: StructuredLogReplayCursor.TryParse(value, out var cursor),
            Bounded: value.Length <= StructuredLogReplayCursor.MaxLength,
            Length: value.Length))
        .ToArray();
}

public sealed record StructuredLogsCursorEvidence(bool Present, bool Valid, bool Bounded, int Length);
