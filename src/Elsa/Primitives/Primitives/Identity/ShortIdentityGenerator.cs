using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Identity;

/// <summary>
/// Generates short, time-ordered 64-bit identifiers rendered as 11 Base62 characters (for example <c>0Fk3Qp9Xb2C</c>).
/// </summary>
/// <remarks>
/// The value packs a 42-bit millisecond timestamp (relative to <c>2020-01-01T00:00:00Z</c>, valid until ~2159) in the
/// high bits and 22 random bits in the low bits. Ordinal string comparison therefore orders identifiers by creation
/// millisecond; identifiers created within the same millisecond are unordered relative to each other.
///
/// No coordination or configuration is required, which makes this a good fit for interactively created entities such as
/// workflow and activity definitions. Because uniqueness within a millisecond relies on 22 random bits (~4.2M values),
/// collision probability rises with very high per-millisecond insert rates; prefer <see cref="SnowflakeIdentityGenerator"/>
/// for high-throughput or multi-node identifier generation.
/// </remarks>
public sealed class ShortIdentityGenerator(ISystemClock systemClock) : IIdentityGenerator
{
    private static readonly long EpochMs = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private const int RandomBits = 22;

    public string Generate()
    {
        var elapsedMs = (ulong)(systemClock.UtcNow.ToUnixTimeMilliseconds() - EpochMs);
        var random = (ulong)Random.Shared.NextInt64(1L << RandomBits);
        var value = (elapsedMs << RandomBits) | random;
        return Base62.Encode(value);
    }
}
