# Feature Specification: First Embedded runtime starting profile

**Feature Branch**: `codex/2052-embedded-runtime-profile`

**Created**: 2026-09-25

**Status**: Implemented — #2052 first Embedded profile and file candidate path; live deployment remains separate

**Input**: [Story #2052](https://github.com/elsa-workflows/elsa-foundation/issues/2052), the [first-profile decision](../../docs/reports/runtime-composition/first-profile-decision.md), [deployable-lock proof](../../docs/reports/runtime-composition/embedded-lock-host-proof.md), and [activation-mapping decision](../../docs/reports/runtime-composition/activation-mapping-boundary.md).

## User Scenarios & Testing

### User Story 1 - Choose and inspect the Embedded starter (Priority: P1)

A developer chooses `embedded-runtime@1` and sees the exact 16 features, the reason each was selected, and what is still unknown about the target host. The profile is an immutable starting point, so they can add or remove individual features while retaining an exact, reviewable selection.

**Why this priority**: A short starting choice only helps if it does not conceal the actual feature set or silently change later.

**Independent Test**: Plan the published profile twice, inspect all 16 IDs and their provenance, then remove one member and verify that the resulting plan retains the removal and explains the missing required dependency.

**Acceptance Scenarios**:

1. **Given** the published profile and a matching pinned composition, **when** the developer plans it, **then** the plan contains exactly the reviewed 16 feature IDs and shows that `WorkflowsRuntimeApi` and `FileSystemDistributedLocking` are selected.
2. **Given** a newer catalog or a modified definition with the same ID and version, **when** the old composition is read, **then** it either resolves against its exact pinned definition or refuses a digest mismatch; it never silently upgrades.
3. **Given** an explicit removal of a required member, **when** the developer plans the selection without a host inventory, **then** the member stays removed and a reviewed required-edge finding blocks materialization.

### User Story 2 - Prepare a reviewable host candidate (Priority: P1)

A developer starts with a host configuration and a pinned profile selection, reviews the exact feature changes, and generates a fresh candidate for one shell and environment. Existing settings and other environments remain intact. Unknown package and host facts stay marked unverified.

**Why this priority**: A profile that can only be inspected but cannot safely reach a host file does not yet reduce configuration effort.

**Independent Test**: Generate a candidate for a host with object-shaped feature configuration; compare selected and unselected files, then re-read the candidate and compare its exact effective feature set with the accepted plan. Repeat with an unsupported source shape and require refusal without output.

**Acceptance Scenarios**:

1. **Given** an accepted 16-feature selection and a supported host source, **when** the developer generates a candidate, **then** the selected shell/environment has exactly those 16 enabled IDs, with a redacted explanation of additions and disables.
2. **Given** a selected base feature carrying settings that the authored selection removes, **when** a selected-environment candidate is generated, **then** that feature is disabled there while its base settings and other environments remain unchanged.
3. **Given** an unsupported or ambiguous source shape, an enablement that the selected overlay cannot achieve, or a required member removed, **when** generation is requested, **then** it refuses before publishing a candidate and explains the blocker.
4. **Given** no target-host inventory, **when** a candidate is generated, **then** host/package compatibility remains unverified rather than reported ready.

### User Story 3 - Supply host prerequisites and run the example (Priority: P2)

A developer supplies a named SQLite persistence resource, an isolated writable lock directory, and host-owned signing configuration separately from the profile. In the demonstrated generic host, they activate the exact selected features and execute, suspend, resume, and complete a workflow.

**Why this priority**: The starter must be deployable in one concrete host, while keeping host-specific and secret values out of shared selection metadata.

**Independent Test**: Activate the exact pinned set in the existing generic-host fixture with the deployable lock provider and one named SQLite resource; verify a bookmark resume and completion, and inspect that no HTTP listener is created by that host.

**Acceptance Scenarios**:

1. **Given** the exact selection and all required host-owned prerequisites, **when** the demonstrated generic host starts, **then** it activates the set and a persisted workflow can suspend, resume, and complete.
2. **Given** a missing lock provider or invalid exact selection, **when** activation is requested, **then** the host refuses rather than silently repairing the authored selection.
3. **Given** the published profile or shareable plan, **when** it is exported, **then** it contains no connection value, signing secret, lock path, or migration authorization.

### Edge Cases

- The selected source uses an array, mixed object/array layers, or case-equivalent duplicate IDs.
- A base feature is explicitly disabled and cannot be re-enabled by the selected overlay.
- Disabling an overlay-only object would discard settings that cannot safely be moved.
- A runtime descriptor conflicts with reviewed dependency metadata; the observed runtime descriptor remains authoritative and the conflict remains visible.
- Process overrides, package loading, and database readiness can differ from the generated files.

## Requirements

### Functional Requirements

- **FR-001**: Foundation MUST publish one immutable, versioned `embedded-runtime@1` definition with the exact 16 reviewed feature IDs, rationale, and content digest.
- **FR-002**: The definition MUST include `FileSystemDistributedLocking`, `WorkflowsRuntimeApi`, and every reviewed required member, and MUST omit provider, connection, lock-path, signing, and migration values.
- **FR-003**: An authored composition MUST pin the catalog and definition; changed same-version content MUST be rejected and a newer version MUST require explicit re-resolution.
- **FR-004**: Planning MUST show exact IDs, provenance, known required edges, and missing host/package evidence without silently adding features or claiming runtime readiness.
- **FR-005**: A known required edge to an explicitly removed member MUST block candidate generation even when no host inventory is supplied. Observed runtime descriptors MUST remain authoritative when supplied.
- **FR-006**: The candidate workflow MUST support reviewed activation edits for one selected shell/environment when the source mapping is proven, preserve unrelated source and unknown settings, and refuse unsupported mappings.
- **FR-007**: After generation, the candidate MUST be read back and its exact enabled feature IDs MUST match the accepted selection before publication.
- **FR-008**: The candidate review MUST explain additions/disables and unresolved prerequisites without exposing secret-bearing values.
- **FR-009**: The developer documentation MUST state separate persistence, lock, signing, package and migration prerequisites and the demonstrated host's HTTP boundary.
- **FR-010**: An acceptance fixture MUST exercise the exact published profile in a generic host through activation, suspend, resume and completion using a named SQLite resource and deployable file locking.

### Key Entities

- **Published profile**: Immutable reviewed ID, version, digest, exact feature members and rationale.
- **Authored composition**: Pinned profile reference, accepted exact expansion and explicit feature additions/removals.
- **Selection plan**: Exact enabled IDs, provenance, known dependency findings and evidence gaps.
- **Host candidate**: Fresh selected-environment configuration generated from an unchanged reviewed source snapshot.
- **Host prerequisites**: Local named persistence resource, lock path, signing values, packages and migration authorization, each outside the profile.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The published profile resolves to exactly the reviewed 16 IDs on every repeat plan and contains no environment-specific or secret value.
- **SC-002**: Every supported candidate readback matches the accepted exact selection; every known missing required edge and unsupported mapping refuses without a published candidate.
- **SC-003**: Changing a published definition under the same ID/version is rejected, while an existing composition stays pinned when a new version is introduced.
- **SC-004**: The documented generic-host example activates the exact set and completes the suspend/resume workflow with one named resource and an isolated lock directory.
- **SC-005**: A developer can identify the included API feature, required host settings, and unverified host/package facts from the plan and documentation before choosing to deploy.

## Assumptions

- V1 covers one generic-host Embedded example and one selected shell/environment; Worker, Authoring and a web builder remain separate.
- The profile chooses features only. Persistence resolution, setting insertion, package delivery, deployment, live reload and migrations retain their existing owners.
- A generated file candidate is not proof of live activation; host runtime descriptors and process overrides must be rechecked at activation time.
- The demonstrated generic host has no HTTP listener, but the selected Runtime API feature can matter in a web host.
