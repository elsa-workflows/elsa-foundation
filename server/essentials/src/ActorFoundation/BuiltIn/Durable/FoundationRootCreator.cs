using System.Security.Cryptography;
using System.Text.Json;
using Kernel.Runtime;

namespace Kernel.Durable;

internal static class FoundationRootCreator
{
    internal static string OperationDigest(string id) =>
        Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(id)));

    internal static bool IsGeneratedReference(string? reference) =>
        reference is { Length: 64 } && reference.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") < 0;

    internal static string ReferenceFor(byte[] issuerKey, string tenant, CreationAddress address)
    {
        var id = Convert.ToBase64String(DurableKeyJson.CodeUnits(address.Id));
        var encoded = JsonSerializer.SerializeToUtf8Bytes(new { Tenant = tenant, Id = id, address.Creation });
        return Convert.ToHexString(HMACSHA256.HashData(issuerKey, encoded));
    }

    internal static Message Create(InstanceRef address, string id, string digest, StartSpec spec, long? due, string? replyTo) =>
        new(id, address, Kinds.Start, spec.Input, Start: spec with { Creation = new(1, 0, digest) },
            Due: due, ReplyTo: replyTo, TargetCreation: 1)
        {
            StorageId = "root:" + address.Id,
            StoreStartInputOnce = true
        };
}
