using System.Text;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>A protocol pin for runtime-owned input admission to one ordinary activity invocation.</summary>
/// <remarks>The pin grants no admission or execution authority. A host must support the exact protocol,
/// reducer, callback and coherent persistence boundary before accepting input. Absent on legacy contracts.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ActivityInvocationInputCapability
{
    [JsonConstructor]
    public ActivityInvocationInputCapability(string protocol, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        // Default JSON replaces unpaired UTF-16 surrogates. Refuse them before the pin can
        // collapse distinct declarations into the same wire value and schema fingerprint.
        try { _ = new UTF8Encoding(false, true).GetByteCount(protocol); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("An invocation-input protocol must use well-formed Unicode.", nameof(protocol), exception);
        }
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        Protocol = protocol;
        Version = version;
    }

    public string Protocol { get; }
    public int Version { get; }
}
