# Feature Specification: Studio UX integrated qualification

**Feature Branch**: `agent/codex/3075-final-qualification`
**Created**: 2026-10-25
**Status**: In progress
**Input**: Deliver epic #3057 end to end, retaining every delivery gate.

Owning work unit: [#3075](https://github.com/elsa-workflows/elsa-foundation/issues/3075).
Program: [Elsa 4 Simplification](../../docs/program-goals/elsa-4-simplification.md).
The issue's finite nine-row composed matrix is authoritative; this spec does not weaken or duplicate its acceptance state.

## User Scenarios & Testing

### User Story 1 - Author and resume a workflow (Priority: P1)

An author can create or resume an exact draft, configure ordinary and advanced HTTP options, test it and publish the reviewed content.

**Why this priority**: This is the normal authoring golden path.
**Independent Test**: Complete rows 1–2 of #3075 on normal Workbench, including real HTTP 200, exact snapshot binding, panel preferences, narrow/keyboard checks and existing Flowchart/BPMN smoke.

**Acceptance Scenarios**:
1. **Given** an ordinary authenticated composition, **When** the author creates, saves, resumes, tests and publishes, **Then** the exact selected draft/content and durable published version are evidenced without equating transient and durable IDs.
2. **Given** a resized editor, **When** the author changes selection or reopens panels, **Then** reachable controls and preferred-width restoration are preserved.

### User Story 2 - Investigate and operate a run (Priority: P1)

An operator enters through Needs attention, finds the exact run and loaded non-sensitive evidence, and takes only a supported next step.

**Why this priority**: Incorrect run/evidence association or premature Retry is harmful.
**Independent Test**: Complete rows 3 and 8, including actual scheduler attempts, permitted intervention, engine trace and authenticated separate-origin logs.

**Acceptance Scenarios**:
1. **Given** an active incident, **When** the operator follows Needs attention, **Then** exact run/activity/evidence identities, current permission and supported action remain associated through return navigation.
2. **Given** automatic retries are pending, **When** the scheduled attempt becomes due, **Then** real UTC/journal/attempt evidence proves execution without manufactured clocks or duplicate operator Retry.
3. **Given** denied, empty, unavailable or error states, **When** diagnostics are opened, **Then** each state is reported truthfully and server authorization stays authoritative.

### User Story 3 - Configure resources and browse inventories (Priority: P2)

A user can configure a service or secret with required inputs first, discover advanced options, recover from failure and understand inventory coverage.

**Why this priority**: These operations support daily authoring and operation.
**Independent Test**: Complete rows 4–6 with disposable data, actual server search/counts, native inbound pointer/keyboard interaction and documented host boundaries.

**Acceptance Scenarios**:
1. **Given** a minimal or configured form, **When** it is invalid or fails, **Then** values, masking, focus, confirmations and reachable actions are preserved.
2. **Given** more than 100 secrets, **When** a later item is searched for, **Then** real server search finds it and shown/loaded/total and loaded-only facets remain distinct.
3. **Given** inventory navigation, **When** a row, copy, disclosure or editor-return control is used, **Then** exact identity/context are retained; unsupported folder/Move and cursor recovery retain their actual boundaries.

### User Story 4 - Adjust settings and author consistent modules (Priority: P3)

Users can change dashboard settings and review feature drafts; module authors have source-faithful examples.

**Why this priority**: Consistency must survive later module work.
**Independent Test**: Complete rows 7 and 9 and reconcile the existing guidance and all UX-01–18 findings.

**Acceptance Scenarios**:
1. **Given** widget settings, **When** Tab, Escape, Cancel or invalid Save is used, **Then** focus, validation and the footer work correctly.
2. **Given** Studio or Server feature changes, **When** review is cancelled or controlled private Apply succeeds, **Then** exact selected configuration/draft/revision and visit retention/clearing are proved independently.
3. **Given** a module author, **When** the existing examples are followed, **Then** available Field/Button/DataGrid/disclosure patterns and deliberate domain exceptions are clear using the public UI package while keeping host-only components private.

### Edge Cases

- Long names, 320/360/390px fallbacks, laptop sizes, effective zoom, light/dark and supported contrast/reduced-motion states.
- Stale requests, actual failed operations, permissions, unavailable modules, cursor invalidation/expiry and unloaded inventory.
- Transient Test Run IDs versus durable publication IDs; handler faults versus overall workflow status.
- Private credentials, raw payloads and secret values must never enter evidence.

## Requirements

### Functional Requirements

- **FR-001**: Verify all nine composed rows and cross-cutting controls in #3075 on the final candidate, retaining every failure/skip/unavailable outcome.
- **FR-002**: Use disposable normal Workbench/Studio compositions, exact source/build provenance, classified writes and owned cleanup.
- **FR-003**: Reproduce a suspected product defect before editing; correct confirmed in-scope defects and verify the change with a meaningful regression and causal control.
- **FR-004**: Update existing normative guidance/examples and map UX-01–18 to accepted evidence or explicit unresolved issues.
- **FR-005**: Preserve [Studio ADR0022 navigation placement](../../Studio/docs/adr/0022-let-the-user-choose-where-the-navigation-sits.md), authorization, domain operations, backend contracts and optional-module boundaries.
- **FR-006**: Complete independent review, exact-head checks, separate application approval/merge and resulting-main gates before closing the issue or epic.

### Key Entities

- **Qualification row**: One bounded composed journey with exact provenance, observed outcomes and acceptance evidence.
- **Evidence receipt**: Safe metadata and asset/screenshot hashes linked to an exact source/build and cleanup result.
- **Audit finding**: UX-01–18, mapped to its implementation record and final qualification.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every one of the nine required journeys has accepted, reviewable evidence; none is inferred from a merged leaf.
- **SC-002**: All 18 audit findings map to accepted checks or explicit unresolved work; unresolved blockers prevent closure.
- **SC-003**: All required delivery gates pass on the reviewed candidate and its resulting main; skipped/cancelled cases retain their real outcomes.
- **SC-004**: No disposable credential/secret value enters evidence, and every owned host/browser/fixture is cleaned up.

## Assumptions

- All 24 implementation prerequisites are accepted as recorded on #3057/#3071.
- Source-only preflight and preliminary baseline attempts are not final acceptance.
- No production deployment, optional provider activation, new module references, navigation redesign or performance benchmark program is included.
