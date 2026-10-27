# Feature Specification: Studio Composition Qualification

**Feature Branch**: `agent/codex/3364-studio-composition-qualification`
**Created**: 2026-10-27
**Status**: In progress
**Input**: Program3340 task3364: qualify a custom packaged module, optional hosting compositions, cold authentication, live Console Stream through shell reload and theme persistence.

## User Scenarios & Testing

### User Story 1 - Add an independent module (Priority: P1)

A developer adds a module through its package and public Studio contracts, without copying the reference application's implementation.

**Why this priority**: Reusable hosting must support developers' own modules, beyond the included Dashboard.
**Independent Test**: Publish an external host and open the custom module's contribution from its packaged browser assets.

**Acceptance Scenarios**:
1. A selected custom module appears in discovery, registers navigation and a routed contribution, and renders at its direct link using the shared browser runtime.
2. Its published resources match the actual archive; a missing required resource fails qualification and restored bytes pass again.

### User Story 2 - Choose optional hosting features (Priority: P1)

A developer chooses base hosting, themes, package administration or the reference composition without hidden dependencies on omitted features.

**Independent Test**: Exercise minimal, themes-only, administration-only and reference profiles independently.

**Acceptance Scenarios**:
1. Each profile boots, serves its selected module and exposes only installed/selected features.
2. An omitted optional package adds neither its services nor its mutation routes; requests to absent routes return404 rather than a missing-service failure.
3. A custom host filter may replace an admitted package-asset result without exhausting response permits for later ordinary requests.

### User Story 3 - Retain security and state through lifecycle changes (Priority: P1)

A user's first protected request is authorized correctly, a live console connection survives a real shell replacement, and managed theme state survives restart.

**Independent Test**: Use cold published processes, a controlled session backend, actual package reconciliation and persisted theme storage.

**Acceptance Scenarios**:
1. Before discovery warms a shell, anonymous/invalid sessions receive401, insufficient permissions403 and permitted sessions succeed; server management credentials never reach browser projections or ordinary session validation.
2. A real console connection receives identifiable records before and after a verified serving-generation replacement, then disconnects cleanly.
3. Managed theme definitions/material/default selection remain readable after restart while directory sources remain distinct and unchanged.

### Edge Cases

Absent addons or disabled shell features; disabled module policy; direct module links; undeclared package dependencies; unavailable session backend; trusted hub query-token paths versus ordinary endpoints; unenrolled native stores; replaced endpoint-filter results; failed reloads; stale/retained generations; source-versus-managed theme ownership.

## Requirements

### Functional Requirements

- **FR-001** Use actual package archives, public composition/SDK contracts and application-owned module selection; no Web reference or copied host implementation.
- **FR-002** Qualify a representative custom module's discovery, contribution, shared runtime and packaged resources in an external published consumer.
- **FR-003** Qualify minimal, themes-only, administration-only and reference compositions, including omitted services/routes and explicit feature/module enablement.
- **FR-004** Prove cold authorization and permission outcomes through real HTTP, trusted query-token boundaries and server-only credential handling.
- **FR-005** Prove a live network console stream across actual serving-generation replacement, with observable pre/post records and clean disconnect.
- **FR-006** Prove persisted managed theme/material/default state through process restart and browser behavior, preserving read-only source ownership.
- **FR-007** Reproduce the recorded endpoint-filter response-ownership scenario against an actual enrolled native store; fix confirmed leakage with fail-before/pass-after evidence while preserving bounded admission.
- **FR-008** Bind consumers to archive/cache/published hashes, isolated locked restore, hidden build inputs and a zero consumer frontend-tool audit. Runtime package feeds are intentional deployed inputs and are distinguished from hidden build feeds.
- **FR-009** Preserve specific failure/control/restoration results and supported-native refusals; arbitrary errors, skips or scripted reconciliation are not successful qualification.
- **FR-010** Complete affected whole suites, architecture/discovery/maps checks, independent review, exact-head CI and resulting-main artifact verification before delivery.

### Key Entities

Composition profile; module package; contribution; validated session; serving generation; live console connection; managed theme state; qualification report. These retain their existing meanings; no new production model or persistence contract is introduced.

## Success Criteria

- **SC-001** All four chosen compositions publish and execute their intended HTTP/browser journeys from declared packages, with zero unexpected missing resources or consumer frontend-tool invocations.
- **SC-002** Every omitted-feature and cold authorization case yields its expected outcome without hidden service resolution or credential exposure.
- **SC-003** The observed console connection remains usable across a demonstrably different serving generation, and theme state remains correct after restart.
- **SC-004** Replaced admitted responses no longer block a later ordinary asset request; a causal regression fails before the correction and passes after it.
- **SC-005** Reports distinguish successful consumers, deliberate failed controls, restored runs, reviewed source delivery and actual-main verification.

## Assumptions and Boundaries

Prerequisites3359/3360/3362/3363 are delivered on verified main26f0893468320500d56fa26dc3fcd42db63a36ba. Only3364 is claimed. Existing hosting, security, theme and protected-store contracts govern; no new architecture or public API is presumed. Actual native immutable/extracted/enrolled profiles are required for mutable package-store access. Unqualified arbitrary filesystem mutation/link cycles remain outside this leaf. No performance measurement, release/deployment/publisher activation, new test project or unrelated program is included. CI automation remains3365 and the supported developer sample/exit audit3366.
