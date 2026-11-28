using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Durable;

internal static class TerminalOutputEncoding
{
    internal static TerminalOutput? Capture(JsonObject output, int limit)
    {
        var names = Encode(limit, (writer, _) =>
        {
            writer.WriteStartArray();
            foreach (var (name, _) in output)
                WriteString(writer, name);
            writer.WriteEndArray();
        });
        if (names is null || names.AsSpan().SequenceEqual("[]"u8)) return null;
        return new(Encoding.UTF8.GetString(names), Encode(limit, (writer, buffer) => WriteValue(writer, buffer, output)));
    }

    private static byte[]? Encode(int limit, Action<Utf8JsonWriter, BoundedBuffer> write)
    {
        using var buffer = new BoundedBuffer(limit);
        try
        {
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = 64 });
            write(writer, buffer);
            writer.Flush();
            return buffer.ToArray();
        }
        catch (Exception error) when (error is LimitExceeded or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Output capture is optional. Unsupported values, encoding exceptions and output beyond the
            // JSON depth or byte budget must leave an otherwise valid actor end intact.
            return null;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, BoundedBuffer buffer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject properties:
                writer.WriteStartObject();
                foreach (var (name, value) in properties)
                {
                    CheckPropertyName(name, buffer.Remaining - writer.BytesPending);
                    writer.WritePropertyName(name);
                    WriteValue(writer, buffer, value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray items:
                writer.WriteStartArray();
                foreach (var item in items)
                    WriteValue(writer, buffer, item);
                writer.WriteEndArray();
                break;
            case JsonValue value when value.TryGetValue<JsonElement>(out var element):
                // A parsed scalar already carries its JSON bytes. Inspect their length before the writer copies them.
                var bytes = JsonMarshal.GetRawUtf8Value(element);
                if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array ||
                    bytes.Length > buffer.Remaining - writer.BytesPending) throw new LimitExceeded();
                writer.WriteRawValue(bytes, skipInputValidation: true);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                WriteString(writer, text);
                break;
            case JsonValue value when value.TryGetValue<object>(out var scalar) &&
                scalar is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or
                    char or DateTime or DateTimeOffset or Guid or DateOnly or TimeOnly or TimeSpan or Half:
                // Serialize known CLR scalars with the default options. A JsonValue can carry its
                // own converter, even for a primitive, which need not obey this writer's value budget.
                JsonSerializer.Serialize(writer, scalar, scalar.GetType());
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                // A custom CLR converter can emit an arbitrary value. The qualified Native outputs are JSON.
                throw new LimitExceeded();
        }
        if (writer.BytesPending >= 16 * 1024) writer.Flush();
    }

    private static void WriteString(Utf8JsonWriter writer, string value)
    {
        // The writer can otherwise allocate storage for a whole escaped string token before a Stream observes its size.
        const int chunk = 4096;
        var rest = value.AsSpan();
        while (rest.Length > chunk)
        {
            // The segmented writer keeps partial Unicode sequences in the same token.
            writer.WriteStringValueSegment(rest[..chunk], isFinalSegment: false);
            writer.Flush();
            rest = rest[chunk..];
        }
        writer.WriteStringValueSegment(rest, isFinalSegment: true);
        writer.Flush();
    }

    private static void CheckPropertyName(ReadOnlySpan<char> name, long remaining)
    {
        // There is no segmented property-name writer. The default JSON encoder emits at most six ASCII bytes per code unit.
        if (remaining < name.Length + 4L) throw new LimitExceeded();
        if (remaining >= 6L * name.Length + 4) return;
        var available = remaining - 4; // Opening and closing quotes, colon and a possible comma.
        Span<char> encoded = stackalloc char[1024];
        while (!name.IsEmpty)
        {
            JavaScriptEncoder.Default.Encode(name, encoded, out var consumed, out var written, isFinalBlock: true);
            available -= written;
            if (available < 0) throw new LimitExceeded();
            name = name[consumed..];
        }
    }

    private sealed class LimitExceeded : Exception;

    private sealed class BoundedBuffer(int limit) : MemoryStream
    {
        internal long Remaining => limit - Length;

        public override void Write(byte[] buffer, int offset, int count)
        {
            RequireSpace(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            RequireSpace(buffer.Length);
            base.Write(buffer);
        }

        private void RequireSpace(int count)
        {
            if (count > Remaining) throw new LimitExceeded();
            if (Length + count > Capacity)
                Capacity = (int)Math.Min(limit, Math.Max(Length + count, Math.Max(256, Capacity * 2L)));
        }
    }
}
