using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Computes the canonical hash used to bind reviewed reusable-activity fork material.</summary>
public static class ActivityForkMaterialFingerprint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Compute(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}
