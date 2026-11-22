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

    private static IncrementalHash VersionedHash(byte domain)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([domain, 2]);
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
