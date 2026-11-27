using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;

namespace Kernel.Foundation.Postgres;

// PostgreSQL notification data is visible to other database users. The frame authenticates its
// destination and sequence, and encrypts the reply with a separate key derived from the store issuer.
internal sealed class PostgresReplyFrames(byte[] key)
{
    private const int ChunkBytes = 5_500;
    private const int MaxReplyBytes = 16 * 1024 * 1024;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int Overhead = NonceBytes + TagBytes;

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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new WireReply(reply, kind));
        if (bytes.Length > MaxReplyBytes)
            throw new InvalidDataException("The live PostgreSQL reply exceeds the receiver's message size limit.");

        var count = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
        var id = Guid.NewGuid();
        var frames = new string[count];
        using var cipher = new AesGcm(key, TagBytes);
        for (var index = 0; index < count; index++)
        {
            var header = FormattableString.Invariant($"{id:N}/{index}/{(index == count - 1 ? '1' : '0')}/");
            var offset = index * ChunkBytes;
            var data = bytes.AsSpan(offset, Math.Min(ChunkBytes, bytes.Length - offset));
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
                var wire = JsonSerializer.Deserialize<WireReply>(bytes);
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
