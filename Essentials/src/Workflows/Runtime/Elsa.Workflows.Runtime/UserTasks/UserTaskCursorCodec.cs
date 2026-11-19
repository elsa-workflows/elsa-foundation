using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

// Cursors carry scan positions of permission-denied rows. Encrypt them and bind every query dimension.
// Replicas use an explicit cursor key or a domain-separated durable recovery key. Development fallback is ephemeral.
internal sealed class UserTaskCursorCodec(TimeProvider time, IOptions<UserTaskOptions>? options = null,
    IOptions<RuntimeRecoveryContinuationOptions>? recovery = null)
{
    private readonly byte[] _key = Key(options?.Value.CursorEncryptionKey, recovery?.Value);
    private static byte[] Key(string? configured, RuntimeRecoveryContinuationOptions? recovery)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try {
                var key = Convert.FromBase64String(configured);
                if (key.Length == 32) return key;
            } catch (FormatException) { }
            throw new InvalidOperationException("User Task CursorEncryptionKey must be a Base64-encoded 32-byte key shared by API replicas.");
        }
        if (!string.IsNullOrWhiteSpace(recovery?.SigningKey))
        {
            if (Encoding.UTF8.GetByteCount(recovery.SigningKey) < 32)
                throw new InvalidOperationException("The shared runtime recovery signing key must contain at least 32 UTF-8 bytes.");
            return SHA256.HashData(Encoding.UTF8.GetBytes("elsa.user-tasks.cursor/1\0" + recovery.SigningKey));
        }
        if (recovery?.AllowEphemeralDevelopmentKey == false)
            throw new InvalidOperationException("User Task paging requires a shared cursor or runtime recovery key.");
        return RandomNumberGenerator.GetBytes(32);
    }
    internal string? Encode(string? after, string context)
    {
        if (after is null) return null;
        var clear = JsonSerializer.SerializeToUtf8Bytes(new Position(after, context, time.GetUtcNow().AddMinutes(15)));
        var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[clear.Length]; var tag = new byte[16];
        using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, clear, cipher, tag);
        return Convert.ToBase64String([..nonce, ..tag, ..cipher]);
    }
    internal string? Decode(string? cursor, string context)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length > 8192) throw new FormatException();
            var bytes = Convert.FromBase64String(cursor); if (bytes.Length < 29) throw new FormatException();
            var clear = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), clear);
            var position = JsonSerializer.Deserialize<Position>(clear);
            if (position is null || position.Context != context || position.ExpiresAt < time.GetUtcNow()) throw new FormatException();
            return position.After;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        { throw new UserTaskException("validation", "This inbox page has expired or changed. Return to the first page."); }
    }
    private sealed record Position(string After, string Context, DateTimeOffset ExpiresAt);
}
