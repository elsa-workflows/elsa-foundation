using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>
/// Writes received RabbitMQ message bodies as JSON Lines, one line per message in queue order (spec 1201), so the run reads
/// them with the JSON Lines source's record rules.
/// </summary>
/// <remarks>
/// A body with no line break is written as received, so the reader judges it exactly as a JSON Lines record. A JSON body
/// spread over several lines is rewritten compactly. A body that has line breaks and is not JSON, or is blank, becomes a line
/// the reader rejects as <c>etl.jsonl.record-invalid</c>, so every message stays one record and none is silently dropped.
/// </remarks>
internal static class EtlRabbitMqSnapshotWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly byte[] LineFeed = [(byte)'\n'];
    // A control character outside any JSON value: never valid JSON and never blank.
    private static readonly byte[] InvalidRecord = [0x01];

    public static async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> bodies, Stream destination, CancellationToken cancellationToken)
    {
        foreach (var body in bodies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await destination.WriteAsync(Line(body), cancellationToken);
            await destination.WriteAsync(LineFeed, cancellationToken);
        }
    }

    internal static ReadOnlyMemory<byte> Line(ReadOnlyMemory<byte> body)
    {
        var span = body.Span;
        if (span.IndexOfAny((byte)'\n', (byte)'\r') < 0 && !IsBlank(span))
            return body;
        try
        {
            using var document = JsonDocument.Parse(body);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                document.WriteTo(writer);
            return buffer.ToArray();
        }
        catch (JsonException)
        {
            return InvalidRecord;
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> span) => span.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0;
}
