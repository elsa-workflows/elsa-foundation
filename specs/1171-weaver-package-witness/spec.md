# Feature Specification: Independent Weaver Package Witness

**Feature Branch**: `agent/codex/3438-weaver-package-proof`

**Created**: 2026-10-30

**Status**: Draft

**Input**: Program [#3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), leaf [#3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438): prove that an independently packaged non-workflow module completes the same context, tool, review and observed-application journey as Workflows without changes to Weaver, its host or provider adapters. Use that proof to accept the smallest contribution contract.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add an independently authored resource (Priority: P1)

A module author distributes a non-workflow resource integration using the public contribution surface. A host operator installs that distribution. An authorized Studio user reads its current resource through Weaver, reviews a proposed change and sees the actual owning resource confirm application.

**Why this priority**: The candidate's source examples do not prove that a separate package author can extend Weaver without assistance from its implementation.

**Independent Test**: Build and consume the module outside the product source using retained distribution artifacts. Complete a real read, review and application, then independently reread the owning resource. Compare the host, Weaver and provider artifacts with the baseline taken before installing the module.

**Acceptance Scenarios**:

1. **Given** an ordinary composed host with the generic contribution support, **When** the separately authored module is installed, **Then** its resource identity, bounded context, domain guidance, read tool, proposed operation and review/result presentation become available without edits to the common components.
2. **Given** an authorized user and an exact resource revision, **When** the user asks for a read and then approves a proposed change, **Then** the module's own operations perform the read and change, and the returned outcome agrees with an independent resource observation.
3. **Given** the same contribution support and host, **When** the user performs the supported native workflow layout journey, **Then** the visible working state changes only after review and observed acknowledgement, normal validation runs, whole-change undo restores it, and saving or publishing remains explicit.

### User Story 2 - Change module availability safely (Priority: P1)

A host operator adds, removes and restores the resource module through the supported package-loading lifecycle. Studio presents the corresponding available and unavailable contributions without modifying unrelated resources or silently repeating an earlier request.

**Why this priority**: Extension includes the lifecycle of an installed module and its originating client context, not only successful startup.

**Independent Test**: Observe actual package membership and serving generations before addition, after addition, after removal and after restoration. Repeat the positive journeys against the final compatible artifacts and run a genuine incompatible contribution control.

**Acceptance Scenarios**:

1. **Given** a host without the module, **When** its package is added and admitted, **Then** only its contributions appear and the new resource journey works.
2. **Given** an installed module and an outstanding proposal, **When** the originating module or client context disappears, **Then** application is refused or its already-dispatched outcome is explicitly uncertain; no replacement target is selected and no effect is replayed.
3. **Given** an unavailable or incompatible contribution, **When** Studio discovers it or the user requests its operation, **Then** a specific availability/refusal outcome is visible and no success is fabricated.
4. **Given** a previously consumed shell request, **When** module reconciliation remounts the assistant, **Then** the request is not silently submitted again as fresh intent.
5. **Given** the restored compatible module, **When** the positive journeys are repeated, **Then** their observations agree with the final installed artifacts and resource state.

### User Story 3 - Rely on the declared security and effect boundary (Priority: P1)

A user can distinguish a proposal from an applied result and can trust refusal when authority, a resource revision or the available contribution no longer matches the reviewed operation.

**Why this priority**: A package boundary is useful only if it preserves the existing policy and review lifecycle.

**Independent Test**: Change authority after review, advance the owning resource before application, supply malformed completion evidence and remove the original client binding. Observe the owning resource before and after each case.

**Acceptance Scenarios**:

1. **Given** authority that no longer permits the reviewed operation, **When** application is attempted, **Then** the owning operation is refused before its effect.
2. **Given** a newer owning-resource revision, **When** an earlier proposal is applied, **Then** the module rejects the stale target without overwriting the newer state.
3. **Given** missing or malformed result evidence, **When** completion is reported, **Then** Weaver does not report successful application; a possible prior effect remains explicit and is not repeated to obtain confirmation.
4. **Given** an identical accepted execution or acknowledgement delivery, **When** transport repeats it within the supported retention boundary, **Then** the recorded result is returned without another owning mutation.

### User Story 4 - Reproduce the contract decision (Priority: P2)

A module author can run a reusable conformance fixture, identify compatible distributions and understand how to migrate from the preview contribution surfaces. A maintainer can assess the final contract from reproducible evidence rather than a special-case demonstration.

**Why this priority**: The witness must make the next independently authored module practical and keep the public contract no larger than demonstrated needs.

**Independent Test**: Follow the fixture's documented producer/consumer steps from a clean consumer environment, verify artifact identities and dependency closure, and inspect the recorded positive, negative and restored outcomes.

**Acceptance Scenarios**:

1. **Given** the retained candidate distributions, **When** a clean consumer restores, builds, publishes and runs the host and module, **Then** it consumes distributions without product source links or a frontend build toolchain.
2. **Given** the conformance results, **When** the candidate is accepted or amended, **Then** the final proof is repeated against the exact resulting artifacts before contract freeze.
3. **Given** a preview consumer, **When** its author follows the migration guidance, **Then** package, namespace, removed-surface and supported-subset changes are explicit and consistent with the extension catalogs.

### Edge Cases

- A compatible package archive has missing browser assets or an incomplete dependency closure.
- A module is present in a feed but absent, rejected or replaced in the serving generation.
- Addition/removal remounts Studio while a consumed request or a pending review exists.
- A permission, target revision, tool version or originating client lifetime changes after preparation.
- A module reports an applied outcome that its owning resource does not substantiate.
- An effect occurs but its acknowledgement response is lost, or cancellation arrives after dispatch.
- An incompatible control fails for an unrelated restore, network or runtime reason; that failure cannot count as the intended control.
- A producer needs frontend tooling while the ordinary consumer must use its prebuilt distributions.
- A provider protocol fixture is available while live-provider credentials are absent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A separately authored and packaged non-workflow module MUST own its resource identity, bounded context/guidance, read and proposed-change operations, validation, risk, review/result presentation and application semantics.
- **FR-002**: The independent module and workflow example MUST complete their real read/review/application journeys through the same generic contribution lifecycle.
- **FR-003**: Successful application MUST include an owning-resource observation matching the exact reviewed target and outcome. Dispatch, a successful transport response or an unverified module claim alone MUST NOT establish application.
- **FR-004**: Adding, removing and restoring the module MUST require zero implementation edits to Weaver, the Studio host or provider adapters. Their baseline identities MUST be retained and compared across that extension journey.
- **FR-005**: The proof MUST use real distribution artifacts and clean consumer dependency closure. Product project references, workspace/source imports and copies of the common implementation MUST NOT make the consumer succeed.
- **FR-006**: The supported consumer MUST restore, build, publish and run with prebuilt browser assets; producing a new module's own assets remains the module author's responsibility.
- **FR-007**: Missing and incompatible contributions MUST produce explicit availability or refusal outcomes while the remaining Studio composition remains usable.
- **FR-008**: Current authority, exact target/revision and originating context MUST be rechecked at the applicable execution/completion boundary. Pre-dispatch denial or staleness MUST leave the owning resource unchanged.
- **FR-009**: Malformed evidence, a vanished client context and an uncertain post-dispatch result MUST NOT become fabricated success or an automatic replay. Identical accepted delivery MUST preserve the supported duplicate-handling boundary.
- **FR-010**: Module reconciliation MUST NOT silently redeliver a previously consumed shell request as new intent.
- **FR-011**: Workflow participation MUST retain normal validation, atomic unsaved working-state application, whole-change undo and explicit save/publish.
- **FR-012**: The reusable fixture MUST retain exact producer/module/consumer identities, versions, dependencies and serving observations. Expected failing controls MUST remain recorded as failures with separate recognition of their intended cause.
- **FR-013**: Any demonstrated generic contract correction MUST be justified by the journey, preserve module ownership and be followed by the complete final artifact proof. The contribution contract MUST NOT be frozen before that proof and accepted #3437 integration verification.
- **FR-014**: Migration guidance and extension catalogs MUST describe actual preview compatibility and the demonstrated workflow subset, linking broader authoring/troubleshooting to #3442.
- **FR-015**: Deterministic composition, provider-protocol and live-provider evidence MUST remain distinct. This work MUST NOT activate a publisher, deploy/release, purchase provider access or extract credentials.
- **FR-016**: Common discovery, orchestration, transport and review components MUST treat module payloads as opaque apart from the declared generic envelope. The proof MUST demonstrate that the independent module alone interprets its resource payload, with no prearranged domain branch in the baseline common components. Provider protocol fixtures MUST remain separate from the resource's behavior.

### Key Entities

- **Distribution set**: The immutable compatible product, module and consumer artifacts, together with their source identities, versions and dependency expectations.
- **Participating resource**: The non-workflow resource selected for the proof, including its identity, revision, authority and independently observable state.
- **Prepared operation**: The module-owned exact target, arguments, validation and risk bound to user review through the common lifecycle.
- **Observed outcome**: The module's typed result and owning-resource evidence, distinguished from a pending or uncertain dispatch.
- **Serving generation**: The actual installed module membership and admitted contributions observed before and after package changes.
- **Conformance record**: Reproducible positive, failing-control and restored observations supporting the smallest accepted contract.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: One independently packaged non-workflow resource and the workflow example each complete a context/read → proposal/review → observed-application journey against their actual owning state.
- **SC-002**: The module can be added, removed and restored with zero changes to the baseline common-component implementations; contribution presence and actual serving membership agree at every transition.
- **SC-003**: Every required authority, stale-target, malformed-result, incompatible-module and vanished-context control has the specified refusal/uncertainty and owning-state observation. No control reports false application or repeats an accepted effect.
- **SC-004**: The clean host/module consumer succeeds from the retained distributions with zero product-source references and zero consumer frontend build invocations.
- **SC-005**: One reusable fixture reproduces the positive/control/restored evidence against the final artifact set, and all contract/migration/catalog claims are supported before freeze and merge.

## Assumptions

- Program #3428 already authorizes scoped implementation, testing, documentation, review, authorized merges and routine issue refinement. This work adds no release, deployment or credential authority.
- At this leaf's claim, merged candidate #3437 remained open/In review in the integration lane while #3438 occupied the sole implementation lane. Its accepted descendant-main integration is now verified and #3437 is Done ([receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3437#issuecomment-6516702315)). The native dependency retains that completed prerequisite; independent package acceptance and contract freeze remain this leaf's responsibility.
- The participating domain will reuse an existing non-workflow resource where its public owning contracts are practical; otherwise an isolated, separately authored fixture resource will make the ownership and persistence boundary explicit.
- The witness uses ordinary host authentication and isolated owned data/processes. Existing developer hosts and user credential state are preserved.
- The candidate's declared retention/recovery limits remain explicit. Durable recovery and full current-module/workflow coverage belong to M2–M5.
- Trusted operator module configuration may select packages/features. The actual extension sequence keeps the compiled host, generic engine and provider artifacts unchanged after its baseline is established.
