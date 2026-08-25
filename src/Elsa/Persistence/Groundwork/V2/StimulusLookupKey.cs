using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Creates fixed-width, collision-resistant lookup keys for opaque stimulus identities.
/// The raw stimulus values remain authoritative in the durable document.
/// </summary>
internal static class StimulusLookupKey
{
    public const int Length = Runtime.ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength;

    public static string FromPair(string stimulusType, string stimulusHash)
    {
        ArgumentNullException.ThrowIfNull(stimulusType);
        ArgumentNullException.ThrowIfNull(stimulusHash);

        // The character-count prefix makes the pair encoding unambiguous even when either value
        // contains delimiters or control characters.
        return Hash($"{stimulusType.Length}:{stimulusType}{stimulusHash}");
    }

    public static string FromType(string stimulusType)
    {
        ArgumentNullException.ThrowIfNull(stimulusType);
        return Hash(stimulusType);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
