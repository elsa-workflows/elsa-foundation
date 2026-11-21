using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int CursorNonceBytes = 12;
    private const int CursorTagBytes = 16;
    private const int ActorRefBytes = 64;
    private readonly Lazy<byte[]> _cursorKey = new(() => RandomNumberGenerator.GetBytes(32));

    internal string SealActorListCursor(ReadOnlySpan<byte> context, string after) =>
        SealReadCursor(context, Encoding.ASCII.GetBytes(after));

    internal bool TryOpenActorListCursor(ReadOnlySpan<byte> context, string? token, out string after)
    {
        Span<byte> plain = stackalloc byte[ActorRefBytes];
        var matched = TryOpenReadCursor(context, token, plain);
        after = matched ? Encoding.ASCII.GetString(plain) : "";
        return matched;
    }

    internal string SealReadCursor(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain)
    {
        Span<byte> bytes = stackalloc byte[plain.Length + CursorNonceBytes + CursorTagBytes];
        var nonce = bytes[..CursorNonceBytes];
        var tag = bytes.Slice(CursorNonceBytes, CursorTagBytes);
        var cipher = bytes[(CursorNonceBytes + CursorTagBytes)..];
        RandomNumberGenerator.Fill(nonce);
        using var protector = new AesGcm(_cursorKey.Value, CursorTagBytes);
        protector.Encrypt(nonce, plain, cipher, tag, context);
        return Convert.ToBase64String(bytes);
    }

    internal bool TryOpenReadCursor(ReadOnlySpan<byte> context, string? token, Span<byte> plain)
    {
        var bytesCount = plain.Length + CursorNonceBytes + CursorTagBytes;
        if (token is null || token.Length != (bytesCount + 2) / 3 * 4) return false;
        Span<byte> bytes = stackalloc byte[bytesCount];
        if (!Convert.TryFromBase64String(token, bytes, out var written) || written != bytes.Length) return false;

        using var protector = new AesGcm(_cursorKey.Value, CursorTagBytes);
        try
        {
            protector.Decrypt(bytes[..CursorNonceBytes], bytes[(CursorNonceBytes + CursorTagBytes)..],
                bytes.Slice(CursorNonceBytes, CursorTagBytes), plain, context);
        }
        catch (CryptographicException)
        {
            return false;
        }
        return true;
    }
}
