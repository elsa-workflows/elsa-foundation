using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Identity;

/// <summary>
/// Generates short, strictly increasing 64-bit Snowflake identifiers rendered as 11 Base62 characters.
/// </summary>
/// <remarks>
/// The value is composed of a 41-bit millisecond timestamp, a 10-bit worker id, and a 12-bit per-millisecond sequence.
/// Unlike <see cref="ShortIdentityGenerator"/>, uniqueness is guaranteed even under high throughput and across multiple
/// nodes, provided each node is assigned a distinct <see cref="SnowflakeIdentityGeneratorOptions.WorkerId"/>. Identifiers
/// are monotonically increasing per worker and sort chronologically under ordinal string comparison.
///
/// The generator is scoped and supplies the (scoped) clock to the singleton <see cref="SnowflakeIdentitySequence"/>,
/// which owns the shared monotonic state.
/// </remarks>
public sealed class SnowflakeIdentityGenerator(ISystemClock systemClock, SnowflakeIdentitySequence sequence) : IIdentityGenerator
{
    public string Generate() => Base62.Encode((ulong)sequence.Next(systemClock));
}
