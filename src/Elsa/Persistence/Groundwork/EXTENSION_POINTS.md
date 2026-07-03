# Extension points — Persistence.Groundwork (runtime) domain

The Groundwork document-store bridge that persists Elsa runtime state (bookmarks, executables, activity/workflow execution state, durable values, scheduler/operational/control-plane/incident state, checkpoint commits, and the post-commit outbox) for shells that select Groundwork runtime persistence. Contracts are defined and defaulted in this feature; the store implementations map the runtime `.Core` state records onto provider-neutral `Groundwork.Documents` envelopes.

This catalog covers the **schema-versioning** seams added so persisted runtime state can evolve without silently breaking suspended workflows. See [`../../../../docs/serialization.md`](../../../../docs/serialization.md) (**Schema evolution**) for the contract and the sanctioned-exception rationale.

## Override — replacement contracts

Exactly one implementation is active per runtime host (registered with `TryAddSingleton` in `AddGroundworkRuntimeStores()`, so a host may replace either default).

| Contract | Default implementation | Responsibility |
|---|---|---|
| `IGroundworkRuntimeDocumentSerializer` | `GroundworkRuntimeDocumentSerializer` | Owns the frozen bridge `JsonSerializerOptions`; stamps each document with its kind's current schema version on write and enforces the stamp on read (deserialize current, upcast older, fail loudly on unknown/future). The single sanctioned serialization surface for runtime documents — stores must not call `System.Text.Json` directly. |
| `IGroundworkRuntimeDocumentUpcasterRegistry` | `GroundworkRuntimeDocumentUpcasterRegistry` | Indexes contributed upcasters per kind and applies them one version at a time; validates the chain **eagerly at construction** (duplicate step, chain gap, step at/beyond a kind's current version, or an incomplete known-kind chain all fail at startup). |

## Extend — contribution (fan-in)

| Interface | Kind | What to register |
|---|---|---|
| `IGroundworkRuntimeDocumentUpcaster` | Source (per-version migration step) | One implementation per `(DocumentKind, FromVersion)` step. Register any number in the service collection; the registry discovers them all via `IEnumerable<IGroundworkRuntimeDocumentUpcaster>`. Each rewrites content JSON from `FromVersion` to `FromVersion + 1`. |

Adding an upcaster never removes another. When bumping a kind's current version in `ElsaRuntimeDocumentVersions`, register an upcaster for the previous version in the same change (see the evolution checklist in `docs/serialization.md`).

## Schema-version model

Versions live in the Groundwork **envelope** `SchemaVersion` field (already persisted per document), not inside content JSON and not on the domain state records. Per-kind current versions are integers declared in `ElsaRuntimeDocumentVersions` (all `1` today); the legacy manifest-wide stamp `"1.0.0"` parses as version `1` for every kind. A committed golden-fixture suite (`tests/.../Fixtures/v1/*.json`) makes any unversioned change to a persisted state-record shape a test failure.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)
