# Feature Specification: Studio Package CI

**Feature Branch**: `agent/codex/3365-studio-package-ci`
**Created**: 2026-10-27
**Status**: In progress
**Input**: Program #3340 task #3365: verify complete Studio package artifacts and automate the qualified independent consumer journeys without activating publishers.

## User Scenarios & Testing

### User Story 1 - Reject incomplete packages (Priority: P1)

A maintainer reviewing a candidate distribution needs an archive check that detects missing dependencies or browser resources before a consumer discovers the defect.

**Why this priority**: A successful repository build can hide an incomplete distributed package.
**Independent Test**: Check genuine complete candidates, then private copies with a required dependency or resource removed, emptied or changed; only the complete and restored candidates qualify.

**Acceptance Scenarios**:
1. The exact expected package set, common candidate version, declared dependency boundaries and required packaged resources are verified from archive bytes.
2. A missing dependency or missing, empty or mismatched required resource fails with a precise diagnostic; the unchanged original artifacts remain available.
3. Existing identities and the historical consolidation record retain their meaning when later hosting packages are checked.

### User Story 2 - Automate independent consumption (Priority: P1)

A custom-host developer needs CI to exercise the same package-only consumption, browser and lifecycle behavior that qualified reusable hosting.

**Why this priority**: Manual receipts alone do not prevent a later packaging or lifecycle regression.
**Independent Test**: Run the existing successful, causal-failure and restored journeys automatically against one genuine candidate batch, and inspect their separate observations.

**Acceptance Scenarios**:
1. CI runs the standard shell, custom frontend, custom module and optional-host compositions against actual candidates, preserving isolated consumption and zero consumer frontend-tool invocations.
2. Dependency, browser-resource, runtime-configuration, reload and admitted-reader controls fail for their intended reasons; unrelated infrastructure errors never count as successful controls.
3. Required supported-native journeys execute or fail CI. A missing native prerequisite is not a pass or skip.
4. Reports bind each result to its tool, source/candidate provenance and inputs, retain failed controls as failed observations, and leave enough evidence to diagnose a failure.

### User Story 3 - Preserve delivery ownership (Priority: P1)

A maintainer needs stronger validation to coexist with current publishing holds and other active programs.

**Why this priority**: Validation must strengthen delivery without changing who may publish.
**Independent Test**: Verify workflow selection and existing publisher guards alongside the qualification changes.

**Acceptance Scenarios**:
1. Changes to owned package/qualification inputs select the relevant checks.
2. Validation keeps existing package names, version policy, dependency pins and publisher authorization unchanged.
3. Every child process and temporary mutation belongs to this run; other worktrees and original archives remain intact.

### Edge Cases

Extra or duplicate archives; an incomplete dependency group; an empty or modified browser payload; stale generated inputs; mismatched candidate versions; a control failing during restore instead of its intended observation; reconciliation with no genuine package update; consumer tooling accidentally invoked; unavailable native prerequisites; failed child cleanup; and an attempted publisher activation disguised as validation.

## Requirements

### Functional Requirements

- **FR-001** Verify the existing exact Studio package inventory and historical/later-addition distinction, including common candidate version, license and icon.
- **FR-002** Verify required dependency metadata and browser resources from actual archive bytes, using the established producer/resource ownership records.
- **FR-003** Demonstrate that the archive guards reject precise dependency and resource defects and accept restored original inputs.
- **FR-004** Automate the accepted package-only, standard/custom frontend, custom-module and optional-composition journeys against actual candidates.
- **FR-005** Automate meaningful normal/control/restored lifecycle and admitted-reader protection observations, reusing established fixtures and public contracts.
- **FR-006** Require the intended causal state and diagnostic before accepting a failed control; retain its failed status separately from expected-failure recognition.
- **FR-007** Keep required native failures visible and preserve isolated caches, locked restore, archive/published provenance, consumer tool denials and owned process cleanup.
- **FR-008** Select validation when its owned inputs change and retain actionable receipts for both successful and failed runs.
- **FR-009** Preserve existing publishing holds, workflow permissions, package identities, version line and dependency pins; perform no remote publication or deployment.
- **FR-010** Complete applicable whole-suite, architecture/maps, independent review, exact-head CI and resulting-main candidate verification before delivery.

### Key Entities

Candidate distribution; archive requirement; qualification observation; causal control; CI receipt. These are build evidence, not new production domain models or contracts.

## Success Criteria

### Measurable Outcomes

- **SC-001** Every required archive dependency/resource defect in the controlled matrix is rejected, while complete and restored originals pass.
- **SC-002** All required consumer and lifecycle profiles execute automatically with attributable results and zero consumer frontend-tool invocations.
- **SC-003** No arbitrary failure, missing native prerequisite or unrun required profile produces a successful aggregate qualification.
- **SC-004** Existing identity, version and publisher-authorization checks remain satisfied, with no publication or deployment introduced.

## Assumptions

Task #3364 is delivered, including actual packages and current-main qualification at `8083f8e54358f795de1eb66dfd3d2241c85751b1`. Its committed probes and the earlier hosting/API/reuse proofs are the starting point; this task does not redesign their public product contracts. Existing package inventory and producer ownership records are authoritative. No new production API, test project, dependency choice or performance target is required. The developer sample and final program/registry reconciliation remain #3366. Existing unrelated quarantined failures and optional screenshot captures remain explicitly reported rather than called passing qualification.
