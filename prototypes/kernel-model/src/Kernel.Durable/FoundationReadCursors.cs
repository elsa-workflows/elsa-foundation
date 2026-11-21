using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int CursorNonceBytes = 12;
    private const int CursorTagBytes = 16;
    private const int ActorRefBytes = 64;
    private const int CursorBytes = CursorNonceBytes + CursorTagBytes + ActorRefBytes;
    private readonly Lazy<byte[]> _cursorKey = new(() => RandomNumberGenerator.GetBytes(32));

    internal string SealActorListCursor(ReadOnlySpan<byte> context, string after)
    {
        Span<byte> bytes = stackalloc byte[CursorBytes];
        var nonce = bytes[..CursorNonceBytes];
        var tag = bytes.Slice(CursorNonceBytes, CursorTagBytes);
        var cipher = bytes[(CursorNonceBytes + CursorTagBytes)..];
        RandomNumberGenerator.Fill(nonce);
        using var protector = new AesGcm(_cursorKey.Value, CursorTagBytes);
        protector.Encrypt(nonce, Encoding.ASCII.GetBytes(after), cipher, tag, context);
        return Convert.ToBase64String(bytes);
    }

    internal bool TryOpenActorListCursor(ReadOnlySpan<byte> context, string? token, out string after)
    {
        after = "";
        if (token is null || token.Length != (CursorBytes + 2) / 3 * 4) return false;
        Span<byte> bytes = stackalloc byte[CursorBytes];
        if (!Convert.TryFromBase64String(token, bytes, out var written) || written != bytes.Length) return false;

        Span<byte> plain = stackalloc byte[ActorRefBytes];
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
        after = Encoding.ASCII.GetString(plain);
        return true;
    }
}
