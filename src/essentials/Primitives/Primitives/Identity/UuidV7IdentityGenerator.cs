using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Identity;

/// <summary>
/// Generates 128-bit, time-ordered UUIDv7 identifiers (RFC 9562) rendered as 32 lowercase hex characters.
/// </summary>
/// <remarks>
/// The canonical UUIDv7 representation places the millisecond timestamp in the most significant bytes, so the
/// resulting strings sort chronologically under ordinal comparison. This is the closest collision-free,
/// zero-coordination replacement for the existing ULID generator and is the recommended default when shorter
/// 64-bit schemes are not required.
/// </remarks>
public sealed class UuidV7IdentityGenerator(ISystemClock systemClock) : IIdentityGenerator
{
    public string Generate() => Guid.CreateVersion7(systemClock.UtcNow).ToString("N");
}
