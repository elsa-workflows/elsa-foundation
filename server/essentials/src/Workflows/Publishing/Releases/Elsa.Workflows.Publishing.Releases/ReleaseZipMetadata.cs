using System.Buffers.Binary;
using System.Text;
using static Elsa.Workflows.Publishing.Releases.ReleaseManifest;

namespace Elsa.Workflows.Publishing.Releases;

/// <summary>Conservative bounded ZIP32 metadata checks before opening compressed entry streams.</summary>
internal static class ReleaseZipMetadata
{
    internal static IReadOnlyList<string> Validate(byte[] bytes, int maximumEntries)
    {
        var end = -1;
        for (var index = bytes.Length - 22; index >= Math.Max(0, bytes.Length - 22 - ushort.MaxValue); index--)
            if (U32(bytes, index) == 0x06054b50 && index + 22L + U16(bytes, index + 20) == bytes.Length) { end = index; break; }
        if (end < 0 || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0 || U16(bytes, end + 8) != U16(bytes, end + 10)) throw Refuse("invalid_archive");
        var count = U16(bytes, end + 10);
        if (count == ushort.MaxValue || count > maximumEntries) throw Refuse("entry_limit_exceeded");
        var size = U32(bytes, end + 12);
        var offset = U32(bytes, end + 16);
        if (offset + (long)size != end) throw Refuse("unsupported_archive_layout");
        var cursor = checked((int)offset);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            if (cursor + 46L > end || U32(bytes, cursor) != 0x02014b50) throw Refuse("invalid_archive");
            var flags = U16(bytes, cursor + 8);
            var nameLength = U16(bytes, cursor + 28);
            var extraLength = U16(bytes, cursor + 30);
            var commentLength = U16(bytes, cursor + 32);
            var entryEnd = cursor + 46L + nameLength + extraLength + commentLength;
            if (entryEnd > end || U16(bytes, cursor + 34) != 0) throw Refuse("invalid_archive");
            CheckFlags(flags);
            var mode = U32(bytes, cursor + 38) >> 16;
            if ((mode & 0xf000) is not (0 or 0x8000 or 0x4000)) throw Refuse("archive_link_or_device");
            if ((U32(bytes, cursor + 38) & 0x400) != 0) throw Refuse("archive_link_or_device");
            var nameBytes = bytes.AsSpan(cursor + 46, nameLength);
            string name;
            try { name = new UTF8Encoding(false, true).GetString(nameBytes); }
            catch (DecoderFallbackException) { throw Refuse("invalid_archive_name"); }
            if ((flags & 0x800) == 0 && nameBytes.ContainsAnyInRange((byte)128, byte.MaxValue)) throw Refuse("ambiguous_archive_name");
            var (_, isDirectory) = ReleaseTransportPaths.Validate(name);
            if ((mode & 0xf000) == 0x4000 && !isDirectory) throw Refuse("invalid_directory_entry");
            CheckExtra(bytes.AsSpan(cursor + 46 + nameLength, extraLength));
            var local = U32(bytes, cursor + 42);
            if (local + 30L > offset || U32(bytes, checked((int)local)) != 0x04034b50) throw Refuse("invalid_archive");
            var at = (int)local;
            if (U16(bytes, at + 6) != flags) throw Refuse("invalid_archive");
            var localName = U16(bytes, at + 26);
            var localExtra = U16(bytes, at + 28);
            var data = local + 30L + localName + localExtra;
            if (data > offset || data + U32(bytes, cursor + 20) > offset || localName != nameLength ||
                !bytes.AsSpan(at + 30, localName).SequenceEqual(nameBytes)) throw Refuse("invalid_archive");
            CheckExtra(bytes.AsSpan(at + 30 + localName, localExtra));
            names.Add(name);
            cursor = checked((int)entryEnd);
        }
        if (cursor != end) throw Refuse("invalid_archive");
        return names;
    }

    private static void CheckFlags(ushort flags)
    { if ((flags & (1 | 0x40 | 0x2000)) != 0) throw Refuse("encrypted_archive"); }
    private static void CheckExtra(ReadOnlySpan<byte> extra)
    {
        while (!extra.IsEmpty)
        {
            if (extra.Length < 4) throw Refuse("invalid_archive");
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (size + 4 > extra.Length) throw Refuse("invalid_archive");
            // ZIP64 is unnecessary within these bounds. Unix/ASi extras can encode link targets: refuse them.
            if (kind is 1 or 0x000d or 0x756e or 0x9901) throw Refuse("unsupported_archive_metadata");
            extra = extra[(size + 4)..];
        }
    }
    private static ushort U16(byte[] bytes, int at)
    { if (at < 0 || at + 2L > bytes.Length) throw Refuse("invalid_archive"); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)); }
    private static uint U32(byte[] bytes, int at)
    { if (at < 0 || at + 4L > bytes.Length) throw Refuse("invalid_archive"); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)); }
}
