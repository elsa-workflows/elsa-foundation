using System.Text.Json;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>
/// Persists arbitrary .NET strings without asking a database or JSON encoder to interpret malformed
/// UTF-16. Sorting before encoding gives every provider the same deterministic payload.
/// </summary>
internal static class IdentityProviderConfigurationSettingsCodec
{
    public static string Serialize(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var encoded = settings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new[]
            {
                IdentityEntityFrameworkUtf16Codec.Encode(pair.Key),
                IdentityEntityFrameworkUtf16Codec.Encode(pair.Value)
            })
            .ToArray();
        return JsonSerializer.Serialize(encoded);
    }

    public static IReadOnlyDictionary<string, string> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var encoded = JsonSerializer.Deserialize<string[][]>(json)
            ?? throw new FormatException("The persisted Identity provider settings payload is null.");
        var settings = new Dictionary<string, string>(encoded.Length, StringComparer.Ordinal);
        foreach (var pair in encoded)
        {
            if (pair is not { Length: 2 } || pair[0] is null || pair[1] is null)
                throw new FormatException("The persisted Identity provider settings payload is malformed.");
            settings.Add(
                IdentityEntityFrameworkUtf16Codec.Decode(pair[0]),
                IdentityEntityFrameworkUtf16Codec.Decode(pair[1]));
        }

        return settings;
    }
}
