using System.Text;
using System.Text.Json;

namespace Kernel.Durable;

internal static class JobResultEncoding
{
    // Reserve M and F for the projected and unprojected fields of the 16 MiB row.
    private const int Limit = 16 * 1024 * 1024 - 2 * 24 * 1024;
    internal const string Error = "result too large";

    internal static string Value(object? value)
    {
        // A single large string can reach the serializer's own token limit before a buffered write occurs.
        if (value is string text && text.Length > Limit) throw new LimitExceeded();
        return Encode(value);
    }

    internal static string Payload(JobFinished result)
    {
        // The encoded result is a string in a second envelope and its escaping also counts against the cap.
        if ((long)(result.Result?.Length ?? 0) + (result.Error?.Length ?? 0) <= Limit)
        {
            try { return Encode(result); }
            catch (LimitExceeded) { }
        }
        return Json.Write(new JobFinished(result.Key, null, Error));
    }

    private static string Encode(object? value)
    {
        using var stream = new BoundedBuffer();
        JsonSerializer.Serialize(stream, value, value?.GetType() ?? typeof(object));
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    internal sealed class LimitExceeded : Exception;

    private sealed class BoundedBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckSpace(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckSpace(buffer.Length);
            base.Write(buffer);
        }

        private void CheckSpace(int count)
        {
            if (count > Limit - Position) throw new LimitExceeded();
        }
    }
}
