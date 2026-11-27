using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kernel.Durable;

namespace Kernel.Foundation.Postgres;

// PostgreSQL notification data is visible to other database users. The frame authenticates its
// destination and sequence, and encrypts the reply with a separate key derived from the store issuer.
internal sealed class PostgresReplyFrames(byte[] key)
{
    private const int ChunkBytes = 5_500;
    private const int MaxReplyBytes = 16 * 1024 * 1024;
    private const int WireDepth = 64;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int Overhead = NonceBytes + TagBytes;
    private const int StringSegment = 4_096;
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        MaxDepth = WireDepth,
        Converters = { new SegmentedString(), new SegmentedNode() }
    };
    private static readonly JsonSerializerOptions ReaderOptions = new() { MaxDepth = WireDepth };

    private enum ItemKind { Json, Text, Boolean, Creation }
    private sealed record WireReply(Reply Reply, ItemKind Item);

    internal string Channel(string node)
    {
        var address = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(node));
        return "elsa_reply_" + Convert.ToHexStringLower(address.AsSpan(0, 20));
    }

    internal string[] Encode(string channel, Reply reply)
    {
        var kind = reply.Item switch
        {
            string => ItemKind.Text,
            bool => ItemKind.Boolean,
            CreationResult => ItemKind.Creation,
            _ => ItemKind.Json
        };
        var buffer = new ReplyBuffer();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = WireDepth }))
            JsonSerializer.Serialize(writer, new WireReply(reply, kind), WriterOptions);
        var bytes = buffer.Written;

        var count = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
        var id = Guid.NewGuid();
        var frames = new string[count];
        using var cipher = new AesGcm(key, TagBytes);
        for (var index = 0; index < count; index++)
        {
            var header = FormattableString.Invariant($"{id:N}/{index}/{(index == count - 1 ? '1' : '0')}/");
            var offset = index * ChunkBytes;
            var data = bytes.Slice(offset, Math.Min(ChunkBytes, bytes.Length - offset));
            var frame = new byte[Overhead + data.Length];
            var nonce = frame.AsSpan(0, NonceBytes);
            RandomNumberGenerator.Fill(nonce);
            cipher.Encrypt(nonce, data, frame.AsSpan(Overhead), frame.AsSpan(NonceBytes, TagBytes), AssociatedData(channel, header));
            frames[index] = header + Convert.ToBase64String(frame);
        }
        return frames;
    }

    internal Reader Open(string channel) => new(channel, new AesGcm(key, TagBytes));

    private static byte[] AssociatedData(string channel, string header) => Encoding.ASCII.GetBytes(channel + "/" + header);

    private static InvalidDataException TooLarge() =>
        new("The live PostgreSQL reply exceeds the receiver's message size limit.");

    private static void WriteString(Utf8JsonWriter writer, string value)
    {
        var remaining = value.AsSpan();
        while (remaining.Length > StringSegment)
        {
            writer.WriteStringValueSegment(remaining[..StringSegment], isFinalSegment: false);
            writer.Flush();
            remaining = remaining[StringSegment..];
        }
        writer.WriteStringValueSegment(remaining, isFinalSegment: true);
    }

    private static void WriteName(Utf8JsonWriter writer, string name)
    {
        if (name.Length <= StringSegment)
        {
            writer.WritePropertyName(name);
            return;
        }

        var space = MaxReplyBytes - writer.BytesCommitted - writer.BytesPending - 4;
        if (name.Length > space) throw TooLarge();
        // Charge the exact escaped size before encoding a large name, so the writer can reserve its UTF-8 bytes directly.
        Span<char> escaped = stackalloc char[6 * StringSegment];
        var rest = name.AsSpan();
        while (!rest.IsEmpty)
        {
            var count = Math.Min(StringSegment, rest.Length);
            if (count < rest.Length && char.IsHighSurrogate(rest[count - 1])) count--;
            var encoded = JavaScriptEncoder.Default.Encode(rest[..count], escaped, out var consumed, out var written, isFinalBlock: true);
            if (encoded != OperationStatus.Done || (space -= written) < 0) throw TooLarge();
            rest = rest[consumed..];
        }
        writer.WritePropertyName(JsonEncodedText.Encode(name));
    }

    private sealed class SegmentedString : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => WriteString(writer, value);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => WriteName(writer, value);
    }

    private sealed class SegmentedNode : JsonConverter<JsonNode>
    {
        public override bool CanConvert(Type type) => typeof(JsonNode).IsAssignableFrom(type);
        public override JsonNode Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, JsonNode value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case JsonObject properties:
                    writer.WriteStartObject();
                    foreach (var (name, item) in properties)
                    {
                        WriteName(writer, name);
                        WriteItem(item);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonArray items:
                    writer.WriteStartArray();
                    foreach (var item in items) WriteItem(item);
                    writer.WriteEndArray();
                    break;
                case JsonValue scalar when scalar.TryGetValue<JsonElement>(out var element):
                    var raw = JsonMarshal.GetRawUtf8Value(element);
                    if (raw.Length > MaxReplyBytes) throw TooLarge();
                    writer.WriteRawValue(raw, skipInputValidation: true);
                    break;
                case JsonValue scalar when scalar.TryGetValue<string>(out var text):
                    WriteString(writer, text);
                    break;
                default:
                    value.WriteTo(writer, options);
                    break;
            }

            void WriteItem(JsonNode? item)
            {
                if (item is null) writer.WriteNullValue();
                else Write(writer, item, options);
            }
        }
    }

    private sealed class ReplyBuffer : IBufferWriter<byte>
    {
        // A writer may reserve space for one escaped segment or padding beyond the bytes it advances.
        private const int CapacityLimit = MaxReplyBytes + 18 * StringSegment + 256;
        private byte[] _buffer = [];
        private int _length;
        internal ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > MaxReplyBytes - _length) throw TooLarge();
            _length += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            var required = (long)_length + Math.Max(sizeHint, 1);
            if (required > CapacityLimit) throw TooLarge();
            if (required > _buffer.Length)
                Array.Resize(ref _buffer, (int)Math.Min(CapacityLimit, Math.Max(required, Math.Max(256L, 2L * _buffer.Length))));
            return _buffer.AsMemory(_length);
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
    }

    internal sealed class Reader(string channel, AesGcm cipher) : IDisposable
    {
        private readonly MemoryStream _data = new();
        private Guid _id;
        private int _next;

        internal Reply? Read(string frame)
        {
            // Base64 can contain slashes, so only the first three delimiters belong to the header.
            if (frame.Length > 7_500 || frame.Split('/', 4) is not [var rawId, var ordinal, var last, var body] || last is not ("0" or "1") ||
                !Guid.TryParseExact(rawId, "N", out var id) || !int.TryParse(ordinal, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                return null;

            Span<byte> encrypted = stackalloc byte[ChunkBytes + Overhead];
            if (!Convert.TryFromBase64String(body, encrypted, out var length) || length <= Overhead)
                return null;
            Span<byte> part = stackalloc byte[ChunkBytes];
            part = part[..(length - Overhead)];
            try
            {
                cipher.Decrypt(encrypted[..NonceBytes], encrypted[Overhead..length], encrypted.Slice(NonceBytes, TagBytes),
                    part, AssociatedData(channel, frame[..^body.Length]));
            }
            catch (AuthenticationTagMismatchException) { return null; }

            if (index == 0)
            {
                _data.SetLength(0);
                _id = id;
                _next = 0;
            }
            if (id != _id || index != _next || _data.Length + part.Length > MaxReplyBytes)
                return null;
            _next++;
            _data.Write(part);
            if (last != "1") return null;

            var bytes = _data.GetBuffer().AsSpan(0, (int)_data.Length);
            try
            {
                var wire = JsonSerializer.Deserialize<WireReply>(bytes, ReaderOptions);
                if (wire?.Reply is not { } reply || reply.Item is not JsonElement value)
                    return wire?.Reply;
                return reply with
                {
                    Item = wire.Item switch
                    {
                        ItemKind.Text => value.GetString(),
                        ItemKind.Boolean => value.GetBoolean(),
                        ItemKind.Creation => value.Deserialize<CreationResult>(),
                        _ => JsonNode.Parse(value.GetRawText())
                    }
                };
            }
            finally
            {
                _data.SetLength(0);
                _next = -1;
            }
        }

        public void Dispose()
        {
            _data.Dispose();
            cipher.Dispose();
        }
    }
}
