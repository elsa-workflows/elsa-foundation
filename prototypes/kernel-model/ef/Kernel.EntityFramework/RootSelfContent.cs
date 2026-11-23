using System.Buffers;
using System.Text;

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
        for (var i = 0; i < value.Length;)
        {
            if (Rune.DecodeFromUtf16(value.AsSpan(i), out var rune, out var units) == OperationStatus.Done)
            {
                offset += rune.EncodeToUtf8(bytes.AsSpan(offset));
                i += units;
            }
            else
            {
                var unit = value[i++];
                bytes[offset++] = 0xff;
                bytes[offset++] = (byte)unit;
                bytes[offset++] = (byte)(unit >> 8);
            }
        }
        return bytes;
    }

    internal static string Decode(byte[] bytes)
    {
        var text = new StringBuilder();
        var start = 0;
        while (start < bytes.Length)
        {
            var marker = Array.IndexOf(bytes, (byte)0xff, start);
            if (marker < 0) break;
            text.Append(StrictUtf8.GetString(bytes, start, marker - start));
            if (marker + 2 >= bytes.Length) throw new FormatException("Incomplete UTF-16 unit.");
            var unit = (char)(bytes[marker + 1] | bytes[marker + 2] << 8);
            if (!char.IsSurrogate(unit)) throw new FormatException("Invalid UTF-16 unit.");
            text.Append(unit);
            start = marker + 3;
        }
        return text.Append(StrictUtf8.GetString(bytes, start, bytes.Length - start)).ToString();
    }
}
