# Feature Specification: Offline composition plan command

**Feature Branch**: `codex/2001-offline-composition-plan`

**Created**: 2026-09-25

**Status**: Implemented — #2001 offline command delivery

**Input**: [Story #2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001), the [reviewed command contract](../../docs/reports/runtime-composition/developer-plan-command-contract.md), and the [implemented selection-planner spec](../174-profile-selection-planner/spec.md).

## User Scenarios & Testing

### User Story 1 - Inspect exact selection before starting a host (Priority: P1)

A developer supplies a pinned catalog and authored composition and sees the exact candidate feature IDs, previously accepted IDs, and why every inclusion or removal occurred. The result is file-only and makes unresolved prerequisites visible.

**Why this priority**: It gives a developer one trustworthy answer to “what will this selection choose?” before any runtime or database action.

**Independent Test**: Run the command against a catalog and authored document with overlapping profile/group membership, an explicit addition, and a removal. Confirm the same exact set and reasons as the shared planner, with no file changes or host process.

**Acceptance Scenarios**:

1. **Given** a profile selecting `A,B`, a group selecting `B,C`, an explicit addition `D`, and a removal `B`, **when** the developer plans, **then** the candidate is exactly `A,C,D`, all selection/removal sources are visible, and the accepted lock remains unchanged.
2. **Given** no host inventory or persistence evidence, **when** the developer plans, **then** the result identifies those checks as unverified and makes no readiness claim.
3. **Given** a safe but unknown feature ID, **when** the developer plans, **then** it remains selected and unresolved; no package or dependency is fabricated.

### User Story 2 - Inspect supplied host and resource evidence (Priority: P2)

A developer may supply a dated target-host inventory and safe resource references. The plan distinguishes observed descriptor dependencies from manifest or reviewed rationale, and tells the developer which facts were merely supplied versus checked live.

**Why this priority**: A short feature selection is misleading unless missing required edges and resource uncertainty are visible.

**Independent Test**: Supply a snapshot with a required descriptor edge to a removed feature, a distinct reviewed optional edge, and one resource reference. Confirm the missing required edge, non-activating optional advice, target/source/time, and unchecked resource status.

**Acceptance Scenarios**:

1. **Given** a loaded `A` whose descriptor requires `B`, and `B` is removed, **when** the developer plans, **then** `B` stays absent and the descriptor-backed missing dependency is unresolved.
2. **Given** optional reviewed or manifest evidence, **when** the developer plans, **then** the advice is labelled by source and never adds a feature.
3. **Given** a supplied `primary` resource reference, **when** the developer plans, **then** `primary` appears only as an unchecked safe name; no provider, connection, or migration safety is inferred.

### Edge Cases

- A loaded descriptor with an empty dependency list is distinct from absent descriptor evidence.
- Duplicate keys, IDs, invalid schema/digest, malformed timestamps, or unknown inventory states refuse input rather than silently defaulting.
- Unknown objects inside authored `settings` or `resources` are neither interpreted nor emitted by the plan.
- A candidate differing from the accepted lock is a proposal, not a silent accepted upgrade.
- An unreadable file or a cancelled command emits no partial JSON result.
- An unsafe identifier or free-text sentinel resembling a credential never appears on stdout or stderr.

## Requirements

### Functional Requirements

- **FR-001**: The command MUST consume explicit versioned catalog and authored-composition files and compute the candidate through the existing shared planner, without another feature-selection algorithm.
- **FR-002**: It MUST show candidate and accepted IDs separately, with safe inclusion/removal provenance and unresolved findings. It MUST NOT modify the authored files or accepted lock.
- **FR-003**: It MUST accept optional versioned workspace-profile and host-inventory files. Invalid or duplicate evidence MUST refuse; absence MUST stay unverified.
- **FR-004**: A supplied resource-reference document MUST accept only safe reference labels and MUST remain unchecked. Caller-supplied status or connection values MUST refuse.
- **FR-005**: The shared plan result MUST expose sorted, source-tagged required/optional dependency evidence, separating reviewed explanation, observed runtime descriptor, and lower-confidence manifest evidence. No evidence row may auto-add a feature.
- **FR-006**: Human and machine-readable outputs MUST derive from the same plan, identify file-only evidence scope, preserve deterministic ordering, and never claim runtime readiness.
- **FR-007**: Opaque authored settings/resources, arbitrary rationale, credentials, secret-bearing paths, parser excerpts, and unsafe identities MUST NOT appear in output or errors.
- **FR-008**: A valid plan, including unresolved findings, MUST succeed; malformed/unsafe input and unreadable input MUST have distinct stable failure classes and no partial machine-readable result.
- **FR-009**: The command MUST NOT load packages, query a feed, start a host or worker, connect to a database, apply migrations, or save configuration.

### Key Entities

- **Authored composition**: The existing pinned user intent and previously accepted expansion; opaque settings/resources remain outside planning.
- **Candidate selection plan**: The shared planner result plus source-tagged dependency evidence.
- **Supplied inventory**: One target/source/time snapshot; it is not proof of current host state.
- **Resource hints**: Safe reference names supplied by a file; their effective bindings are unchecked.
- **Redacted presentation**: Deterministic human or machine-readable view of safe plan facts.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All contract examples return the documented exact feature set and accepted-versus-candidate distinction without changing input files.
- **SC-002**: Every supplied required/optional edge displays its source and selection state; a missing required target never appears in the exact set unless explicitly selected.
- **SC-003**: Semantically equivalent inputs with identical evidence identity/time produce byte-identical machine-readable output in repeated runs.
- **SC-004**: Sentinel credentials placed in every untrusted input field tested appear zero times in standard output, standard error, or the machine-readable plan.
- **SC-005**: Inspection completes without a host, database, package feed, or write permission to the input directory.

## Assumptions

- The [command contract](../../docs/reports/runtime-composition/developer-plan-command-contract.md) fixes the first invocation and output boundary; it is the detailed source for field names, refusal codes, and examples.
- The [selection planner](../174-profile-selection-planner/spec.md) already owns expansion, digests, missing-edge findings, and accepted-lock semantics. This unit only adds dependency evidence to the shared result.
- Export/import, live source precedence, trusted effective-persistence checking, and released starting profiles are separate work.
