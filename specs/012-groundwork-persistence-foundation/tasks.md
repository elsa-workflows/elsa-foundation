# Tasks: Groundwork Persistence Foundation

**Input**: Design documents from `specs/012-groundwork-persistence-foundation/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Phase 1: Setup

- [x] T001 Create Groundwork feature directory at `specs/012-groundwork-persistence-foundation/`
- [x] T002 Create Groundwork specification in `specs/012-groundwork-persistence-foundation/spec.md`
- [x] T003 Create specification quality checklist in `specs/012-groundwork-persistence-foundation/checklists/requirements.md`
- [x] T004 Create implementation plan in `specs/012-groundwork-persistence-foundation/plan.md`

## Phase 2: Foundational Planning

- [x] T005 Capture product-boundary decisions in `specs/012-groundwork-persistence-foundation/research.md`
- [x] T006 Define planning concepts in `specs/012-groundwork-persistence-foundation/data-model.md`
- [x] T007 Define generic/application/Elsa boundary rules in `specs/012-groundwork-persistence-foundation/contracts/groundwork-boundary.md`
- [x] T008 Rewrite original roadmap into Groundwork slices in `specs/012-groundwork-persistence-foundation/contracts/roadmap-slices.md`
- [x] T009 Add validation guide in `specs/012-groundwork-persistence-foundation/quickstart.md`
- [x] T010 Create program-goal bucket in `docs/program-goals/groundwork-persistence-readiness.md`
- [x] T011 Register program-goal bucket in `docs/program-goals/README.md`
- [x] T012 Update active Speckit plan pointer in `AGENTS.md`

## Phase 3: User Story 1 - Define Groundwork As A Generic Product (P1)

**Goal**: Architects can distinguish Groundwork kernel responsibilities from Elsa integration responsibilities.

**Independent Test**: Review the spec, data model, and boundary contract and verify that generic concepts do not require Elsa terminology.

- [x] T013 [US1] Define Groundwork kernel responsibilities in `specs/012-groundwork-persistence-foundation/data-model.md`
- [x] T014 [US1] Define application integration responsibilities in `specs/012-groundwork-persistence-foundation/contracts/groundwork-boundary.md`
- [x] T015 [US1] Define Elsa validation bridge responsibilities in `specs/012-groundwork-persistence-foundation/contracts/groundwork-boundary.md`
- [x] T016 [US1] Record package/root boundary decision in `specs/012-groundwork-persistence-foundation/plan.md`

## Phase 4: User Story 2 - Classify Persistence Workloads (P2)

**Goal**: A persistence designer can classify planned stores before choosing a persistence mechanism.

**Independent Test**: Use `quickstart.md` to classify roadmap candidates into Groundwork default, benchmark-gated, or specialized-provider categories.

- [x] T017 [US2] Define workload classification model in `specs/012-groundwork-persistence-foundation/data-model.md`
- [x] T018 [US2] Add runtime and operational workload guardrails in `specs/012-groundwork-persistence-foundation/contracts/groundwork-boundary.md`
- [x] T019 [US2] Add roadmap candidate classification check in `specs/012-groundwork-persistence-foundation/quickstart.md`

## Phase 5: User Story 3 - Rewrite The Roadmap In Groundwork Terms (P3)

**Goal**: The original Persistence vNext roadmap has an executable Groundwork-first sequence.

**Independent Test**: Compare original S1-S8 to G0-G8 in the roadmap contract and verify every slice is mapped or explicitly deferred.

- [x] T020 [US3] Map original S1-S8 to G0-G8 in `specs/012-groundwork-persistence-foundation/contracts/roadmap-slices.md`
- [x] T021 [US3] Move narrowed Elsa bridge validation earlier as G3 in `specs/012-groundwork-persistence-foundation/contracts/roadmap-slices.md`
- [x] T022 [US3] Preserve runtime go/no-go evaluation as G8 in `specs/012-groundwork-persistence-foundation/contracts/roadmap-slices.md`

## Phase 6: Next Implementation Slice Preparation

- [x] T023 Prepare G1 specification for core manifest and planner kernel after G0 review is accepted
- [ ] T024 Decide whether to create GitHub tracking issues from the Groundwork roadmap after G0 review is accepted
- [ ] T025 Decide whether to keep future execution in this session or hand off G1 to a fresh worker/thread

## Dependencies

- G0 setup and foundational planning tasks are complete.
- G1 must not start until the product boundary, package map, workload taxonomy, manifest vocabulary, and roadmap mapping are accepted.
- GitHub issue creation waits for an explicit tracking decision and Git operating-model preference.

## Parallel Execution Examples

- T013, T014, and T015 can be reviewed independently because they touch separate boundary facets.
- T017 and T020 can be reviewed independently because workload classification and roadmap mapping are separate concerns.
- T023 and T024 should wait for G0 review acceptance.

## Implementation Strategy

G0 is complete when tasks T001-T022 are accepted. The next MVP implementation slice is G1: create the Groundwork core manifest and planner kernel with focused unit tests and no Elsa package references.
