# Feature Specification: First reviewed diagnostics feature group

**Feature Branch**: `codex/2066-diagnostics-group`

**Created**: 2026-09-26

**Status**: Implemented — #2066 published the first reviewed diagnostics group; host readiness remains separate

**Input**: [Story #2066](https://github.com/elsa-workflows/elsa-foundation/issues/2066), [selection-planner spec 174](../174-profile-selection-planner/spec.md), and the [diagnostics two-target proof](https://github.com/elsa-workflows/elsa-foundation/issues/1969).

## User Scenarios & Testing

### User Story 1 - Add and inspect diagnostics (Priority: P1)

A developer starts with the Embedded profile, adds one reviewed diagnostics group, and sees the exact resulting feature IDs and the reason each was selected. They can still remove an individual feature and see any broken required edge.

**Independent Test**: Initialize a composition with `embedded-runtime@1` and `diagnostics-ef@1`; plan it and verify all four group IDs, provenance, and the two EF-to-base required edges. Remove one base ID and verify the missing edge stays unresolved without silent re-addition.

### User Story 2 - Keep existing compositions stable (Priority: P1)

A developer with a composition created under the original catalog continues to plan and generate it without locating an old catalog file. A new composition receives the new catalog pin, and an invalid pin does not silently select a different definition.

**Independent Test**: Plan and generate the same version-1 authored composition before and after the new bundled snapshot; compare exact IDs and pin. Supply a mismatched explicit catalog and an unknown pin, and verify unresolved/refusal behavior.

### User Story 3 - Understand deployment limits (Priority: P2)

An operator sees that a selection group is a convenience for choosing features, while each EF consumer's provider, connection, and migration prerequisites remain separate. A plan without host or persistence evidence stays unverified.

**Independent Test**: Inspect the published catalog and developer reference for the four IDs and absence of secret or database values; inspect a plan without supplied evidence for explicit unverified findings.

## Requirements

- **FR-001**: The reviewed group MUST select exactly `DiagnosticsOpenTelemetry`, `DiagnosticsOpenTelemetryEntityFrameworkCore`, `DiagnosticsStructuredLogs`, and `DiagnosticsStructuredLogsEntityFrameworkCore`, with the two EF-to-base edges marked required.
- **FR-002**: The group MUST be flat, immutable, versioned, and content-addressed. It MUST add no persistent settings-precedence layer and contain no provider, connection, secret, or migration authorization.
- **FR-003**: A new catalog snapshot MUST retain the original catalog and profile definitions unchanged. New compositions may use the new snapshot; existing compositions MUST resolve the exact bundled snapshot named by their ID, version, and digest.
- **FR-004**: The developer init command MUST accept the reviewed group alongside a profile and write its pinned reference and exact accepted feature set. Planning MUST show group provenance and dependency findings without silently adding features.
- **FR-005**: An explicit catalog file MUST remain an explicit choice. Unknown or modified bundled pins MUST remain unresolved or refused; no command may silently upgrade an authored composition.
- **FR-006**: The developer reference MUST distinguish group selection from separate diagnostic resource bindings, host/package compatibility, migration safety, and in-process engine tracing.

## Success Criteria

- **SC-001**: A new profile-plus-group composition has exactly 20 selected IDs: the reviewed 16-member Embedded profile plus the four distinct diagnostic members, with four group provenance entries and two required-edge explanations.
- **SC-002**: An original version-1 composition still plans and generates the same 16 IDs using only bundled content, with no pin-mismatch finding.
- **SC-003**: Removing a required diagnostic base ID leaves it absent and produces an unresolved edge; a changed or unknown pin never becomes an accepted new selection.
- **SC-004**: The published group and shareable plan contain zero provider/connection values or secrets and make no host-ready or migration-ready claim.

## Boundaries and assumptions

This selects server diagnostics only. The separate resource-binding and two-target behavior was proved in #1969 and is unchanged here. The existing pure planner and file candidate bridge remain the implementation boundary; no package loading, database access, host activation, hosted builder, or engine-tracing bridge is delivered by this work unit. Authoring and Worker remain unreleased starting profiles.
