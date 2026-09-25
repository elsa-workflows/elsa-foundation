# Research: First Embedded runtime profile

## Membership

**Decision**: Publish one explicit 16-ID `embedded-runtime@1` definition. The [first-profile decision](../../docs/reports/runtime-composition/first-profile-decision.md) identifies the 15-member closure and Runtime API implication; the [lock proof](../../docs/reports/runtime-composition/embedded-lock-host-proof.md) adds `FileSystemDistributedLocking` and proves the generic-host positive path.

**Rationale**: Every effective member is visible. The file provider is deployable within the tested single-host/local-filesystem boundary.

**Alternatives**: The original 12 authored IDs hid three auto-resolved members. A process-local test lock provider was not a deployable starter.

## Catalog and developer entry point

**Decision**: Commit literal content-addressed catalog data and parse it through the existing strict selection reader. A small developer initialization path creates a composition pinned to the catalog/profile; existing plan and generate commands remain consumers.

**Rationale**: `SelectionCatalog`, `SelectionDefinition`, `SelectionDigest`, and `SelectionReresolver` already enforce version/digest semantics. There is no released Foundation catalog source today.

**Alternatives**: Runtime-synthesized digests would allow edits to a published version; using live feature-management metadata would conflate mutable host evidence with reviewed selection.

## Activation mapping

**Decision**: Support only [proven object-map edits](../../docs/reports/runtime-composition/activation-mapping-boundary.md) to the selected overlay. Add absent ID as `true`; disable a base object with `false`; re-read exact IDs. Refuse base `false` that wins, arrays, setting-destructive removal and ambiguity.

**Rationale**: Pinned CShells tests prove these merge effects; existing bridge is intentionally settings-only.

**Alternatives**: Editing base affects other environments. `Enabled` is a setting, not state. Arrays merge by index.

## Dependency and evidence gate

**Decision**: If no loaded descriptor exists, pinned reviewed required edge can report missing selected dependency. Supplied loaded descriptor remains authoritative, including empty required list. Generation refuses known missing edges; host/package/persistence evidence may stay unverified.

**Rationale**: Existing planner exposes reviewed edges as evidence but without inventory does not block a missing edge. CShells otherwise auto-adds disabled `WorkflowsRuntimeResumption`.

**Alternatives**: A fabricated inventory would make offline generation cumbersome. Refusing every unverified fact would make the file bridge unusable.

## Host values and proof

**Decision**: Keep named SQLite resource, `LocksFolderPath`, signing and migration authorization outside profile. Reuse generic-host proof; no live deployment/readiness claim.

**Rationale**: Values vary by host and can be secret-bearing. Fixture exercises EF/bookmark behavior without HTTP listener, while Runtime API remains selected.
