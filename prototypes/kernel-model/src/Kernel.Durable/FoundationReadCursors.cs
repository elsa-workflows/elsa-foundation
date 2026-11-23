using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int ActorRefBytes = 64;
    private readonly FoundationReadCursor _readCursor = new();

    internal string SealActorListCursor(ReadOnlySpan<byte> context, string after) =>
        SealReadCursor(context, Encoding.ASCII.GetBytes(after));

    internal bool TryOpenActorListCursor(ReadOnlySpan<byte> context, string? token, out string after)
    {
        Span<byte> plain = stackalloc byte[ActorRefBytes];
        var matched = TryOpenReadCursor(context, token, plain);
        after = matched ? Encoding.ASCII.GetString(plain) : "";
        return matched;
    }

    internal string SealReadCursor(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain) => _readCursor.Seal(context, plain);

    internal bool TryOpenReadCursor(ReadOnlySpan<byte> context, string? token, Span<byte> plain) => _readCursor.TryOpen(context, token, plain);
}

internal sealed class FoundationReadCursor(byte[]? key = null)
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    internal const int Overhead = NonceBytes + TagBytes;
    private readonly Lazy<byte[]> _key = new(() => key ?? RandomNumberGenerator.GetBytes(32));

    internal string Seal(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain)
    {
        Span<byte> bytes = stackalloc byte[plain.Length + Overhead];
        var nonce = bytes[..NonceBytes];
        var tag = bytes.Slice(NonceBytes, TagBytes);
        var cipher = bytes[(NonceBytes + TagBytes)..];
        RandomNumberGenerator.Fill(nonce);
        using var protector = new AesGcm(_key.Value, TagBytes);
        protector.Encrypt(nonce, plain, cipher, tag, context);
        return Convert.ToBase64String(bytes);
    }

    internal bool TryOpen(ReadOnlySpan<byte> context, string? token, Span<byte> plain)
    {
        var bytesCount = plain.Length + Overhead;
        if (token is null || token.Length != (bytesCount + 2) / 3 * 4) return false;
        Span<byte> bytes = stackalloc byte[bytesCount];
        if (!Convert.TryFromBase64String(token, bytes, out var written) || written != bytes.Length) return false;

        using var protector = new AesGcm(_key.Value, TagBytes);
        try
        {
            protector.Decrypt(bytes[..NonceBytes], bytes[(NonceBytes + TagBytes)..], bytes.Slice(NonceBytes, TagBytes), plain, context);
        }
        catch (CryptographicException)
        {
            return false;
        }
        return true;
    }
}
