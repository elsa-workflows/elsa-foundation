# Feature Specification: Custom Studio Host Guide and Program Exit

**Feature Branch**: `agent/codex/3366-studio-host-guide`
**Created**: 2026-10-27
**Status**: In progress
**Input**: Program #3340 task #3366: give developers a runnable package-only starting point, explain migration and supported choices, and reconcile the complete program from integrated evidence.

## User Scenarios & Testing

### User Story 1 - Start a custom Studio from packages (Priority: P1)

A developer obtains the small sample and a matching candidate distribution, chooses the application configuration, and restores, publishes and runs Studio without the Elsa source workspace or a frontend build.

**Why this priority**: This is the program's original developer outcome.
**Independent Test**: Follow the published recipe in an isolated folder using genuine distributed packages; observe the standard browser, selected module, discovery and runtime configuration after hiding build inputs.

**Acceptance Scenarios**:

1. Given the sample and explicitly selected matching packages, when the developer restores and publishes, then every Elsa dependency resolves as a package and no application-private infrastructure is copied.
2. Given the published application, when its source, package feed and restore cache are unavailable, then the standard browser and selected module still load and the runtime configuration is assigned.
3. Given a distribution that needs preview sources, when the recipe is followed, then the required sources and version input are explicit and no public-feed-only availability is inferred.

### User Story 2 - Migrate and select optional capabilities (Priority: P2)

A host author replaces copied infrastructure with the supported package entry points while retaining application-owned module selection, configuration and optional capability choices.

**Why this priority**: Reuse should remove boilerplate while preserving the host's deliberate choices.
**Independent Test**: Trace the guide to the accepted public contract, the thin reference host and already qualified independent/combined capability journeys.

**Acceptance Scenarios**:

1. Given a copied Web host, when the migration steps are applied, then reusable hosting, administration and themes have named package homes, while module selection and root-lifetime integrations remain explicit composition choices.
2. Given optional capabilities, when their setup is followed, then authentication, server-only credentials, storage ownership, retention and restart/reload limits are stated without inventing a new coordination or maintenance policy.
3. Given a custom frontend or custom module, when the guide is consulted, then producer responsibilities, fixed browser paths, asset exclusion and feature-selection boundaries are clear.

### User Story 3 - Verify the whole program before closure (Priority: P2)

A maintainer can follow the evidence from accepted boundaries through source delivery, actual packages, browser/server behavior and CI, and see consistent issue and board state.

**Why this priority**: Closure must mean the requested outcome was delivered.
**Independent Test**: Audit all 30 native work items and their hierarchy against delivery receipts and the current merged-source checks.

**Acceptance Scenarios**:

1. Given M0–M3 delivery receipts, when the exit ledger is read, then source, candidate, runtime, review and main-verification evidence are distinguished and linked.
2. Given completed work with stale checkboxes or labels, when its receipt is verified, then issue bodies, native states, board fields and terminal spec records agree.
3. Given unrun, failed, unsupported or deferred cases, when the program closes, then those limits remain visible and no release or deployment is claimed.

### Edge Cases

Missing or mismatched candidate versions; accidentally selecting private control mutants from a CI artifact; preview-source assumptions; a sample that builds only inside the repository; source/cache-dependent published assets; consumer frontend tooling; optional server services omitted; unavailable backend; writable versus read-only configuration; ordinary versus enrolled package stores; legacy retention settings; broken/cyclic/external theme sources; superseded CI or known quarantine failures mistaken for passes; and closing parents before verified children.

## Requirements

### Functional Requirements

- **FR-001** Provide a complete small runnable host sample using public packages and application-owned configuration.
- **FR-002** Document explicit candidate/version/source selection and distinguish original product archives from retained control mutants.
- **FR-003** Prove restore, publish and browser/server startup outside the source workspace, with no project references or consumer frontend build.
- **FR-004** Explain package choices, migration from copied infrastructure, module selection, authentication/backend configuration and storage paths using the accepted contract.
- **FR-005** Cover optional themes/administration, custom frontend/module choices, reload diagnostics and supported native-store/retention boundaries without changing production policy.
- **FR-006** Make the four accepted qualification/contract reports and M0–M3 delivery receipts discoverable.
- **FR-007** Reconcile completed issue/spec records only from actual evidence and preserve explicit failed, skipped, superseded and unsupported observations.
- **FR-008** Close the remaining native hierarchy and update the dedicated Kanban board only after reviewed delivery and resulting-main verification; leave other active programs intact.
- **FR-009** Preserve package identities, dependency pins, publishing holds and the existing authorization/storage/lifetime invariants. No release, deployment or publisher activation.

### Key Entities

Developer recipe, sample inputs, candidate distribution, delivery receipt and work-item state. These describe the existing developer workflow and evidence; no new production domain model is introduced.

## Success Criteria

### Measurable Outcomes

- **SC-001** The sample restores, publishes and serves the standard browser, runtime configuration and selected module from genuine packages in an isolated folder, with zero consumer frontend-tool invocations.
- **SC-002** Every accepted migration/optional-capability handoff has a supported statement and evidence link, with no copied private host class or new production abstraction.
- **SC-003** All four original reports and every M0–M3 milestone have working evidence links.
- **SC-004** All 30 work items have mutually consistent completed issue/body/board records after final delivery, with the supported limits and publication boundary explicit.

## Assumptions

- #3364, #3365 and #3368 are delivered; current main and its fresh actual candidate artifact are verified before this task starts.
- Dashboard is the small starter module already selected in the accepted hosting contract. Optional capabilities remain separate choices.
- A candidate distribution and its explicit preview sources are valid recipe inputs; remote package publication is outside this program.
- A backend may be configured independently. A browser shell starting without a backend is not proof of a deployed Elsa Server journey.
- Existing public contracts and accepted qualification profiles settle the design; no new architecture or technology choice needs research or owner clarification.
