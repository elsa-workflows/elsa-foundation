using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

// Cursors carry scan positions of permission-denied rows. Encrypt them and bind every query dimension.
// Process-local cursors expire on restart; journal state and accepted decisions never depend on this key.
internal sealed class UserTaskCursorCodec(TimeProvider time)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
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
