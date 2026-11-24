using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int ActorRefBytes = 64;
    // Older event keys include an encoded event-match envelope instead of a fixed-length digest.
    private const int MaxInboxCursorBytes = 128 * 1024;
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

    internal string SealActorInboxCursor(ReadOnlySpan<byte> context, ParkPageCursor after) =>
        SealReadCursor(context, Encoding.UTF8.GetBytes(DurableKeyJson.Write(after)));

    internal bool TryOpenActorInboxCursor(ReadOnlySpan<byte> context, string? token, out ParkPageCursor after)
    {
        after = default;
        if (token is null || token.Length % 4 != 0) return false;
        var padding = token.EndsWith("==", StringComparison.Ordinal) ? 2 : token.EndsWith('=') ? 1 : 0;
        var length = token.Length / 4 * 3 - padding - FoundationReadCursor.Overhead;
        if (length is <= 0 or > MaxInboxCursorBytes) return false;

        Span<byte> plain = length <= 4_096 ? stackalloc byte[length] : new byte[length];
        if (!TryOpenReadCursor(context, token, plain)) return false;
        after = DurableKeyJson.Read<ParkPageCursor>(Encoding.UTF8.GetString(plain));
        return true;
    }

    internal string SealReadCursor(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain) => _readCursor.Seal(context, plain);

    internal bool TryOpenReadCursor(ReadOnlySpan<byte> context, string? token, Span<byte> plain) => _readCursor.TryOpen(context, token, plain);
}

internal sealed class FoundationReadCursor(byte[]? key = null)
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int StackBytes = 4_096;
    internal const int Overhead = NonceBytes + TagBytes;
    private readonly Lazy<byte[]> _key = new(() => key ?? RandomNumberGenerator.GetBytes(32));

    internal string Seal(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain)
    {
        var bytesCount = plain.Length + Overhead;
        Span<byte> bytes = bytesCount <= StackBytes ? stackalloc byte[bytesCount] : new byte[bytesCount];
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
        Span<byte> bytes = bytesCount <= StackBytes ? stackalloc byte[bytesCount] : new byte[bytesCount];
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
