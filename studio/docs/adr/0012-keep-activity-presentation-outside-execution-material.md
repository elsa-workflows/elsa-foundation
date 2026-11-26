# Keep Activity Presentation Outside Execution Material

Status: accepted (2026-07-28)

Activity Display Name and Description are authored per workflow occurrence. Foundation stores them
as typed, node-keyed design metadata beside layout, and publication/Test Run copies a frozen
executable-node-keyed projection onto the Source Reference. Studio uses that frozen projection for
historical inspection.

The metadata never enters executable nodes, Execution Material, or `ArtifactHash`. It documents
behavior but does not change behavior. This applies the same source-owned sidecar boundary already
used for layout while avoiding the live-catalog-only naming limitation described in ADR 0010.

## Considered Options

- **Put the fields on authored/executable activity nodes** — rejected because presentation would
  become coupled to runtime compilation and behavioral serialization.
- **Resolve current draft or catalog wording during historical inspection** — rejected because
  historical views would drift and may outlive their source definition.
- **Store Studio-owned opaque JSON in layout records** — rejected because Foundation could not
  validate, preserve, or expose a durable cross-client contract.
- **Store typed design metadata and freeze it per Source Reference** — accepted because it gives
  drafts, versions, Test Runs, and published references the correct lifecycle without changing
  artifact identity.

## Consequences

- Draft and version design APIs gain an additive activity-presentation collection.
- Source References gain an optional frozen collection keyed by executable node ID.
- Existing definitions and references default to no authored presentation and need no generated
  migration values.
- Studio owns one fallback chain: authored/frozen Display Name, catalog display name, technical
  activity type.
- Foundation's companion ADR is `docs/adr/0050-activity-presentation-is-source-owned-metadata.md`.
