using System.Text.Json;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>
/// Turns one received RabbitMQ message body into one JSON Lines snapshot line (spec 1201), so the run reads each message as
/// exactly one record with the JSON Lines source's rules, and the line is never longer than the body (or one byte for an
/// empty body), so the snapshot bound can be checked as messages arrive.
/// </summary>
/// <remarks>
/// A leading UTF-8 byte order mark is dropped, as the JSON Lines reader drops one only at the start of a file; a body that
/// still starts with one becomes a rejected line, because as the snapshot's first line the reader would take that mark for
/// the file's and could read the rest as blank, dropping the message. A body with no
/// line break is written as received. A valid JSON body spread over several lines has each line break replaced by a space,
/// which is insignificant whitespace there. A body that is blank, or has line breaks and is not JSON, becomes a line the
/// reader rejects as <c>etl.jsonl.record-invalid</c>, so no message is silently dropped.
/// </remarks>
internal static class EtlRabbitMqSnapshotWriter
{
    public static readonly ReadOnlyMemory<byte> LineFeed = new byte[] { (byte)'\n' };
    // A control character outside any JSON value: never valid JSON and never blank.
    private static readonly byte[] InvalidRecord = [0x01];
    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    internal static ReadOnlyMemory<byte> Line(ReadOnlyMemory<byte> body)
    {
        if (body.Span.StartsWith(ByteOrderMark))
            body = body[ByteOrderMark.Length..];
        var span = body.Span;
        if (IsBlank(span) || span.StartsWith(ByteOrderMark))
            return InvalidRecord;
        if (span.IndexOfAny((byte)'\n', (byte)'\r') < 0)
            return body;
        try
        {
            using (JsonDocument.Parse(body)) { }
        }
        catch (JsonException)
        {
            return InvalidRecord;
        }
        // Outside strings a line break is whitespace, and valid JSON has none inside them, so a space keeps the value.
        var line = span.ToArray();
        for (var index = 0; index < line.Length; index++)
            if (line[index] is (byte)'\n' or (byte)'\r')
                line[index] = (byte)' ';
        return line;
    }

    private static bool IsBlank(ReadOnlySpan<byte> span) => span.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0;
}
