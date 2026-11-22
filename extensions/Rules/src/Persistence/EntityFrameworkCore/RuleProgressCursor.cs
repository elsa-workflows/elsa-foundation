using System.Buffers.Binary;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Compact provider-defined cursor for one immutable progress ordering position.</summary>
internal static class RuleProgressCursor
{
    private const byte Version = 1;
    private const int EncodedLength = 118;
    private const int CursorLength = 158;

    internal readonly record struct Position(string RunKey, string Fingerprint, long SnapshotRevision,
        long VisibleRevision, RuleProgressItemKind Kind, int Rank, string KeyHash);

    internal static string Encode(string runKey, string fingerprint, long snapshotRevision, long visibleRevision,
        RuleProgressItemKind kind, int rank, string key)
    {
        var bytes = new byte[EncodedLength];
        bytes[0] = Version;
        Convert.FromHexString(runKey).CopyTo(bytes, 1);
        Convert.FromHexString(fingerprint).CopyTo(bytes, 33);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(65, 8), snapshotRevision);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(73, 8), visibleRevision);
        bytes[81] = checked((byte)kind);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(82, 4), rank);
        Convert.FromHexString(RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(key))).CopyTo(bytes, 86);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static bool TryDecode(string cursor, out Position position)
    {
        position = default;
        if (cursor.Length != CursorLength || cursor.Any(static character =>
                character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_'))
            return false;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/') + "==");
        }
        catch (FormatException)
        {
            return false;
        }
        if (bytes.Length != EncodedLength || bytes[0] != Version || bytes[81] > (byte)RuleProgressItemKind.Round)
            return false;

        var snapshotRevision = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(65, 8));
        var visibleRevision = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(73, 8));
        var rank = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(82, 4));
        if (snapshotRevision <= 0 || visibleRevision <= 0 || visibleRevision > snapshotRevision || rank < 0)
            return false;

        position = new(Convert.ToHexString(bytes.AsSpan(1, 32)).ToLowerInvariant(),
            Convert.ToHexString(bytes.AsSpan(33, 32)).ToLowerInvariant(), snapshotRevision, visibleRevision,
            (RuleProgressItemKind)bytes[81], rank, Convert.ToHexString(bytes.AsSpan(86, 32)).ToLowerInvariant());
        return true;
    }
}
