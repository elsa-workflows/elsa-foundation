using System.Text.Json;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Serializes application sets deterministically while preserving arbitrary UTF-16 values.</summary>
internal static class IdentityApplicationSetCodec
{
    public static string Serialize(IReadOnlySet<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var encoded = values
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(IdentityEntityFrameworkUtf16Codec.Encode)
            .ToArray();
        return JsonSerializer.Serialize(encoded);
    }

    public static IReadOnlySet<string> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var encoded = JsonSerializer.Deserialize<string[]>(json)
            ?? throw new FormatException("The persisted Identity application set payload is null.");
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in encoded)
        {
            if (value is null || !values.Add(IdentityEntityFrameworkUtf16Codec.Decode(value)))
                throw new FormatException("The persisted Identity application set payload is malformed or contains duplicates.");
        }

        return values;
    }
}
