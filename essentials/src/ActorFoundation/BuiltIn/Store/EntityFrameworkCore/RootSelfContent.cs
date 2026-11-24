using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace Kernel.EntityFramework;

/// <summary>Exact UTF-8 byte budget for opaque .NET text. An isolated UTF-16 unit occupies three bytes.</summary>
internal static class RootSelfContent
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // FF cannot occur in UTF-8. Its two following bytes preserve an isolated UTF-16 unit verbatim.
    internal static byte[] Encode(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value)];
        var offset = 0;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var result = Utf8.FromUtf16(remaining, bytes.AsSpan(offset), out var read, out var written, replaceInvalidSequences: false);
            if (result == OperationStatus.Done) break;
            if (result != OperationStatus.InvalidData)
                throw new InvalidOperationException("The UTF-8 byte count did not cover the input.");
            offset += written;
            remaining = remaining[read..];
            var unit = remaining[0];
            bytes[offset++] = 0xff;
            bytes[offset++] = (byte)unit;
            bytes[offset++] = (byte)(unit >> 8);
            remaining = remaining[1..];
        }
        return bytes;
    }

    internal static string Decode(byte[] bytes)
    {
        var marker = Array.IndexOf(bytes, (byte)0xff);
        if (marker < 0) return StrictUtf8.GetString(bytes);

        var text = new StringBuilder();
        var start = 0;
        do
        {
            text.Append(StrictUtf8.GetString(bytes, start, marker - start));
            if (marker + 2 >= bytes.Length) throw new FormatException("Incomplete UTF-16 unit.");
            var unit = (char)(bytes[marker + 1] | bytes[marker + 2] << 8);
            if (!char.IsSurrogate(unit)) throw new FormatException("Invalid UTF-16 unit.");
            text.Append(unit);
            start = marker + 3;
            marker = Array.IndexOf(bytes, (byte)0xff, start);
        } while (marker >= 0);
        return text.Append(StrictUtf8.GetString(bytes, start, bytes.Length - start)).ToString();
    }
}
