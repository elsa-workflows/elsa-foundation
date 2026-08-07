# Groundwork Persistence Targets Are Named and Lanes Bind to Them

Status: proposed (2026-08-07)

Tracking: [issue #1156](https://github.com/elsa-workflows/elsa-foundation/issues/1156).
Constrained by [ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md).

## Context

Constitution §E2.2 splits `Elsa.Workflows.*` into Design and Runtime sub-domains "with separate persistence
layers", and §E2.2.3 requires Design-only, Runtime-only and combined deployment shapes to stay supported. The
logical seam was real: runtime provably reads nothing from design at execution time, and CI fails any runtime
project that references a design project.

The persistence substrate could not deliver it. A host admitted exactly one Groundwork physical store, and
that was enforced in eight independent places — a host-global provider-leaf guard, a one-deployment-schema
guard, an initializer-type early return, a single unkeyed `IDocumentStore` registration, a write-once session
source, one merged `elsa-documents` manifest, a compare-exchange-once capability snapshot, and the readiness
task. A "Design-only node" therefore still carried every runtime table, which undercuts the shape's purpose.

Worse, composing a second connection string for the same provider was **silently discarded**: the provider
registration early-returned when its initializer type was already registered, dropping operator configuration
with no diagnostic.

Groundwork itself imposes none of this. It has no DI layer, no static store registry and no ambient context;
stores are factory-constructed objects taking their own connection string, and schema state is persisted in
the target database keyed by `(manifest identity, provider name)`. Nothing there prevents N stores per
process. The constraint was entirely Elsa's.

## Decision

A **Groundwork target** is one admitted physical store: a name, the provider leaf that opens it, and the
schema composed from the lanes bound to it. A host may declare several.

1. **Target names are opaque and operator-chosen.** `default` is the only well-known name — the target a lane
   binds to when it names none. Names are trimmed and compared ordinally, so `design` and `Design` are two
   targets.

2. **Three parties meet at the name and nothing else.** A provider leaf knows how to open a connection and
   its own typed options; it never learns that persistence lanes exist. A domain persistence feature knows
   one target name; it never learns which provider backs it. Composition groups manifest contributions by
   target name and knows neither. An architecture guard enforces that provider leaf projects carry no lane
   vocabulary, because this neutrality erodes quietly rather than loudly.

3. **Declaring a target is the composition guard.** An exact repeat is idempotent, so composing a provider
   twice stays safe. A second, different store under an already-declared name throws and names both. The
   connection string is never retained — a declaration keeps a truncated SHA-256 of it — so a diagnostic can
   identify a store without quoting its credentials.

4. **Composition and admission are per target.** Each target composes only the lanes bound to it, admits only
   their storage units, and derives its own manifest identity: bare `elsa-documents` for `default`, and
   `elsa-documents.{name}` otherwise. Without the derivation, two targets pointed at one database would
   overwrite each other's Groundwork schema-state row. Keeping `default` bare means databases admitted before
   targets existed are unaffected.

5. **A lane bound to a named target never falls back.** If the target is undeclared it throws. The default
   lane does fall back to an ambient unkeyed `IDocumentStore`, which is how a host supplying its own store
   keeps working without declaring a target. Falling back on a *named* target would silently read and write
   the wrong database, which is the failure this whole mechanism exists to prevent.

6. **The unified and runtime-only features remain, as presets.** They declare one target and bind the
   relevant lanes to it. They are a convenience for the one-database case, not a separate composition, and no
   longer the only route to the design lane.

## Consequences

A host can put the authoring catalog and runtime execution state in different databases, on the same or
different providers, and each database carries only its own lane's tables. Design and runtime gain
independent scaling, backup, retention and blast radius, which is what §E2.2.3's deployment shapes were for.

A new domain gains its own database for free: it ships persistence contracts in its core, a Groundwork
implementation in its own library, and a shell feature with a `Target` setting.

**Two operations still require co-located lanes.** Reusable-activity publication commits design, runtime and
publishing documents in one Groundwork transaction, and Groundwork has no cross-store transaction; splitting
those three fails with the lane-to-target mapping named rather than misfiling documents. The dashboard
portfolio tile spans design and runtime and switches to per-target queries with in-memory correlation. The
first of these is the subject of
[ADR 0066](0066-reusable-activity-publication-orders-writes-instead-of-one-transaction.md).

**`Groundwork.Tool` still applies one host-wide schema.** A split host over-provisions each database rather
than applying per-target. That is a tooling gap, not a runtime one: admission still validates per target.

The former one-provider-per-host rule is retired. `SelectGroundworkProviderLeaf` is deleted and its
conformance suite now asserts the per-target contract: a different store claiming an already-declared name is
rejected, a second provider declaring its own name is accepted, and an identical re-declaration is idempotent.
