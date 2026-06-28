# Identity generators

Elsa generates the string ids for entities such as workflow definitions, versions, drafts, activity definitions, and activity node ids through a single seam: `IIdentityGenerator` (`Elsa.Primitives.Contracts.IIdentityGenerator`).

```csharp
public interface IIdentityGenerator
{
    string Generate();
}
```

Because every factory and command resolves ids through this interface, the id format is a swappable policy. The integrator picks the strategy at composition time; no entity code changes.

## Why the format matters

Ids are stored as strings and used as primary keys, so two properties matter for database performance and ergonomics:

- **Sortability** — a time-ordered id keeps inserts at the "end" of the index B-tree, avoiding page splits and fragmentation. Random ids (plain GUIDs) scatter inserts across the index and fragment it.
- **Length** — shorter ids mean smaller indexes, smaller rows, and friendlier URLs.

**Default:** both the EF Core and Groundwork persistence features default to `Short` — a short (~11 char), time-ordered, Base62 id requiring no coordination. This replaced the previous defaults (ULID, 26 chars, on EF Core; a random non-sortable GUID, 32 chars, on Groundwork). The generators below let you opt into different guarantees (for example `UuidV7` for collision-free 128-bit ids, or `Snowflake` for high-throughput / multi-node uniqueness).

## Built-in generators

All generators return strings whose **ordinal** comparison reflects creation order (except `Guid`).

| Kind | Type | Bits | Output | Length | Sortable | Coordination | Notes |
|------|------|------|--------|--------|----------|--------------|-------|
| `Short` | `ShortIdentityGenerator` | 64 | Base62 | 11 | yes (to the ms) | none | 42-bit ms timestamp + 22 random bits. Best for interactively created entities. **Active default.** |
| `UuidV7` | `UuidV7IdentityGenerator` | 128 | hex | 32 | yes | none | RFC 9562 v7. Collision-free, drop-in replacement for ULID. |
| `Snowflake` | `SnowflakeIdentityGenerator` | 64 | Base62 | 11 | yes (strictly increasing per worker) | per-node worker id | 41-bit ms + 10-bit worker id + 12-bit sequence. Safe at high throughput / multi-node. |
| `Guid` | `GuidIdentityGenerator` | 128 | hex | 32 | **no** | none | Random GUID. Provided for parity; not recommended for indexed keys. |
| ULID | `EFCoreIdentityGenerator` | 128 | base32 | 26 | yes | none | The previous EF Core default. Still available where the `Ulid` package is referenced (the EF Core persistence project). |

The catalog generators live in `Elsa.Primitives` under the `Elsa.Primitives.Identity` namespace (so every persistence layer can register them), while the `AddIdentityGenerator` selection helper lives in `Elsa.Primitives.Hosting`.

### Choosing one

- **Want the smallest change, collision-free, no ops?** Use `UuidV7`. It is the standard, modern replacement for ULID. Shorter than ULID only modestly, but zero risk.
- **Want genuinely short ids (~11 chars) with no configuration?** Use `Short`. Collision probability is negligible for design-time entities (created interactively) but rises with very high per-millisecond insert rates.
- **Want short ids that stay unique under high throughput or across multiple nodes?** Use `Snowflake` and assign each node a distinct `WorkerId` (0–1023).
- **Don't care about sortability?** `Guid` exists, but prefer a time-ordered option for indexed primary keys.

## Selecting a generator

Call `AddIdentityGenerator` from the host **after** the persistence feature has registered its default; the call replaces any prior `IIdentityGenerator` registration.

```csharp
using Elsa.Primitives.Hosting.Extensions;
using Elsa.Primitives.Identity;

// Short, time-ordered, no coordination — this is the active persistence default.
services.AddIdentityGenerator(IdentityGeneratorKind.Short);

// UUIDv7 — collision-free 128-bit, zero coordination.
services.AddIdentityGenerator(IdentityGeneratorKind.UuidV7);

// Snowflake — assign a distinct worker id per node (e.g. from configuration / env var).
services.AddIdentityGenerator(IdentityGeneratorKind.Snowflake, options =>
{
    options.WorkerId = Environment.GetEnvironmentVariable("ELSA_NODE_ID") is { } id ? long.Parse(id) : 0;
    // options.Epoch = ...; // optional; defaults to 2020-01-01Z
});
```

### Lifetimes

`UuidV7`, `Short`, and `Guid` are stateless and registered **scoped** (matching the existing defaults and the scoped `ISystemClock`). `Snowflake` keeps monotonic state (last timestamp + sequence) in a **singleton** `SnowflakeIdentitySequence`, while the generator itself stays scoped and supplies the clock per call — so there is no captive-dependency on the scoped clock.

## Adding your own

Implement `IIdentityGenerator`, then register it last so it wins:

```csharp
services.RemoveAll<IIdentityGenerator>();
services.AddScoped<IIdentityGenerator, MyIdentityGenerator>();
```

If your id is time-ordered, encode it so that ordinal string comparison matches numeric order. `Base62` (fixed 11-char width, ascending alphabet) in the `Identity` namespace is the helper the built-in 64-bit generators use for exactly this.

## Format compatibility with Groundwork

This catalog deliberately mirrors the [Groundwork](https://github.com/valence-works/Groundwork) library's `Groundwork.Core.Identity` catalog — same Base62 alphabet/width, same epoch (`2020-01-01Z`), and the same Snowflake bit layout — so ids produced by either codebase are format-compatible. Because the two are independent copies, that compatibility is a convention, not an automatic invariant; it is pinned by golden-value tests (`IdentityFormatCompatibilityTests`) that exist with **identical literals** in both repos. If you change an epoch, bit split, or alphabet here, update Groundwork's copy and its golden test to match.
