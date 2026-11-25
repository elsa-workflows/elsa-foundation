using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Canonical, order-stable hash of the provider-neutral designer layout sidecar.</summary>
public static class ActivityLayoutHasher
{
    public static string Compute(IEnumerable<ActivityLayoutRecord> records)
    {
        using var frame = new MemoryStream();
        var canonical = records
            .Select(x => new { x.NodeId, Data = CanonicalJson(x.Data) })
            .OrderBy(x => x.NodeId, StringComparer.Ordinal)
            .ThenBy(x => x.Data, StringComparer.Ordinal);
        foreach (var record in canonical)
        {
            Append(record.NodeId);
            Append(record.Data);
        }
        return $"sha256-{Elsa.Serialization.Core.JsonHashInput.HashFramed(frame.ToArray())}";

        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            frame.Write(length);
            frame.Write(bytes);
        }
    }

    private static string CanonicalJson(JsonElement element)
    {
        return Encoding.UTF8.GetString(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(element));
    }
}
