# ADR 0044: `sourceKind` is canonical on executable inspection contracts

- Status: Accepted
- Date: 2026-07-13

## Context

Runtime API v1 executable-inspection responses expose both `sourceType` and `sourceKind`. They are populated
from the same `WorkflowExecutableSourceReference.SourceKind` value. Two equivalent names leave client authors
without an authoritative field and create a risk that the fields evolve independently.

## Decision

`sourceKind` is the canonical source discriminator for executable summaries, details, provenance, and source
references. Runtime API v1 continues to emit `sourceType` as a read-only compatibility alias so existing clients
do not break. The alias always has the same JSON value as `sourceKind` and is marked obsolete in the public .NET
contract.

New clients, generated clients, documentation, and Studio code use only `sourceKind`. Writers must not accept or
interpret `sourceType` as independent input because executable-inspection contracts are read-only.

The compatibility alias is removed when the Runtime API capability advances to version 2. That removal is a
major contract change and must ship together with the capability-version change and its consumer migration notes;
it must not be removed from a version 1 response.

## Compatibility contract

| Runtime API capability | `sourceKind` | `sourceType` |
| --- | --- | --- |
| v1 | Canonical, required for new clients | Deprecated response alias; value equals `sourceKind` |
| v2 | Canonical | Removed |

## Consequences

- Existing v1 consumers keep working without ambiguous server behavior.
- New consumers have one documented field and can ignore the alias.
- Contract tests prevent the alias from diverging before its versioned removal.
- Removing `sourceType` requires an explicit Runtime API v2 release rather than an incidental DTO cleanup.
