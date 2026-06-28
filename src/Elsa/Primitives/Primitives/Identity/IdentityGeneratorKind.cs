namespace Elsa.Primitives.Identity;

/// <summary>
/// The built-in <see cref="Elsa.Primitives.Contracts.IIdentityGenerator"/> strategies that can be selected via
/// <c>AddIdentityGenerator</c>.
/// </summary>
public enum IdentityGeneratorKind
{
    /// <summary>128-bit, time-ordered UUIDv7 (32 hex chars). Collision-free, zero coordination.</summary>
    UuidV7,

    /// <summary>Short 64-bit, time-ordered, Base62 (~11 chars). No coordination; best for interactively created entities. The active persistence default.</summary>
    Short,

    /// <summary>Short 64-bit Snowflake, Base62 (~11 chars). Requires a per-node worker id; safe at high throughput.</summary>
    Snowflake,

    /// <summary>Random 128-bit GUID (32 hex chars). Not sortable; not recommended for indexed keys.</summary>
    Guid
}
