using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kernel.Durable;

internal static class FoundationEventIdentity
{
    internal static string Match(EventRow publication, Guid incarnation)
    {
        using var hash = VersionedHash((byte)'M');
        AppendName(hash, publication.Key);
        AppendName(hash, publication.Id);
        AppendIncarnation(hash, incarnation);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string DeliveryId(EventRow publication, SubscriptionRow wait, Guid incarnation)
    {
        using var hash = VersionedHash((byte)'D');
        AppendName(hash, publication.Id);
        AppendIncarnation(hash, incarnation);
        AppendName(hash, wait.Instance);
        Span<byte> creation = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(creation, wait.Creation);
        hash.AppendData(creation);
        AppendName(hash, wait.Wait);
        return "ev:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string CatchUpId(InboxKey first)
    {
        using var hash = VersionedHash((byte)'C', 1);
        AppendName(hash, first.Id);
        AppendNumber(hash, first.Scope);
        hash.AppendData([first.Match is null ? (byte)0 : (byte)1]);
        if (first.Match is { } match) AppendName(hash, match);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string CatchUpSuccessor(string chain, long page)
    {
        using var hash = VersionedHash((byte)'N', 1);
        AppendName(hash, chain);
        AppendNumber(hash, page);
        return "meet:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendNumber(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static IncrementalHash VersionedHash(byte domain, byte version = 2)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([domain, version]);
        return hash;
    }

    private static void AppendName(IncrementalHash hash, string name)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, name.Length);
        hash.AppendData(length);
        hash.AppendData(DurableKeyJson.CodeUnits(name));
    }

    private static void AppendIncarnation(IncrementalHash hash, Guid incarnation)
    {
        Span<byte> value = stackalloc byte[16];
        incarnation.TryWriteBytes(value, bigEndian: true, out _);
        hash.AppendData(value);
    }
}
