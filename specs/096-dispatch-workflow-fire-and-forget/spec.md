# Feature Specification: Dispatch a Published Workflow Fire-and-Forget

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Approved

**Input**: GitHub issue #676, “Dispatch a published workflow fire-and-forget”, under parent #674

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a pinned workflow dispatch (Priority: P1)

As a workflow author, I can add a Foundation-native `DispatchWorkflow` activity, select an accessible workflow definition with a live Published artifact through the ordinary activity-input options UI, and publish the parent with that exact child artifact pinned into its executable.

**Why this priority**: A static, publication-pinned target is the deterministic authoring contract on which every runtime lifecycle slice depends.

**Independent Test**: Discover the activity from its dedicated module, request options for `WorkflowDefinitionId`, publish a parent selecting one returned definition, and verify that the parent executable contains the selected live Published child artifact identity.

**Acceptance Scenarios**:

1. **Given** the dedicated module is enabled, **When** the activity catalog is built, **Then** one Foundation-native logical activity type named `DispatchWorkflow` is discoverable with the agreed inputs, outputs, defaults, editor metadata, and outcomes.
2. **Given** accessible and inaccessible definitions with and without live Published artifacts, **When** options are requested for `WorkflowDefinitionId`, **Then** only accessible definitions with a live Published artifact are returned through the existing generic options contract.
3. **Given** a selected definition with a live Published artifact, **When** the parent is published, **Then** the selected artifact is minimally pinned into the immutable parent executable for runtime use.

---

### User Story 2 - Continue after durable dispatch responsibility (Priority: P1)

As a workflow author using the default fire-and-forget mode, I receive a deterministic child workflow execution ID and the `Dispatched` outcome as soon as the parent checkpoint durably records responsibility for starting the child, without waiting for child materialization.

**Why this priority**: This is the first complete user-visible tracer bullet and establishes the required checkpoint-before-delivery boundary.

**Independent Test**: Execute the activity with `WaitForCompletion` omitted, pause delivery after checkpoint commit, and verify that the parent activity is completed with `Dispatched`, the reserved child ID is available, one Pending dispatch record and one child-start intent are durable, and the parent can continue while no child execution state exists yet.

**Acceptance Scenarios**:

1. **Given** a valid pinned child artifact and child input values, **When** `DispatchWorkflow` runs with its default settings, **Then** one parent checkpoint atomically persists activity completion, `Dispatched`, `ChildWorkflowExecutionId`, a Pending dispatch record, and a child-start post-commit intent.
2. **Given** that checkpoint has committed but post-commit delivery has not run, **When** the parent drain continues, **Then** downstream parent work may advance and no child workflow execution is required to exist.
3. **Given** the same parent workflow execution and activity execution are delivered repeatedly, **When** dispatch state is recorded, **Then** every attempt converges on one dispatch record, one child execution identity, and one child-start intent.
4. **Given** two distinct activity executions, **When** each dispatches the same child artifact, **Then** each receives a distinct child execution identity and dispatch record.

---

### User Story 3 - Start the child asynchronously through runtime seams (Priority: P1)

As a host developer, I can let the global runtime resumption path deliver the child-start intent through the existing workflow start dispatcher so the configured execution actor provider chooses local or distributed execution without transport choices in the activity.

**Why this priority**: The activity is complete only when durable responsibility becomes a real asynchronous child start through existing Foundation runtime composition.

**Independent Test**: Run an in-memory parent and child through checkpoint commit plus global resumption, assert that the parent continues before child materialization, then assert that exactly one logical child executes through the configured actor provider with the expected pinned artifact and inherited context.

**Acceptance Scenarios**:

1. **Given** a deliverable child-start intent, **When** the global post-commit/resumption pump processes it, **Then** the contributed handler invokes the existing workflow start dispatcher outside workflow execution actor mailboxes.
2. **Given** duplicate delivery of the same child-start intent, **When** the handler retries, **Then** dispatch converges on the reserved child workflow execution identity rather than creating a second logical child.
3. **Given** no correlation override, **When** the child start request is built, **Then** it inherits the parent correlation identity; an explicit override establishes the requested child correlation identity.
4. **Given** a parent tenant or partition, execution-authority snapshot, run kind, and root initiator provenance, **When** the child is started, **Then** the child inherits the parent boundary and run kind, represents the parent execution as its system identity, and retains the root initiator only for audit provenance.

### Edge Cases

- `WaitForCompletion=true` is part of the stable activity contract but its success/result/resume behavior is delivered by #679; this slice must not silently behave as fire-and-forget for that value.
- `CancelChildOnParentCancellation` defaults to `true` but has no effect in fire-and-forget mode; detached children remain independent of ordinary later parent cancellation.
- Child values flow only through the workflow-input channel. Variables, stimulus, tenant, authority, run-kind, execution IDs, and other reserved runtime metadata cannot be supplied through `Inputs`; validation against declared child input names is hardened in #677.
- An empty optional correlation override uses inheritance rather than creating a blank correlation boundary.
- #676 starts the exact pinned artifact/source while that source remains live; retained-pin execution after replacement or unpublication is hardened with hashing and retention in #677.
- Child-start delivery failure remains owned by the existing outbox failure path; retry exhaustion, permanent failure/dead-letter behavior, and operational redrive are delivered by #681.
- The in-memory end-to-end path is asynchronous but does not claim survival across process failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A dedicated activity runtime module MUST expose one Foundation-native logical activity type named `DispatchWorkflow` without Elsa Core wire-compatibility aliases.
- **FR-002**: The activity MUST expose inputs `WorkflowDefinitionId`, `Inputs`, `WaitForCompletion` defaulting to `false`, `CancelChildOnParentCancellation` defaulting to `true`, and optional `CorrelationId`.
- **FR-003**: The activity MUST expose `ChildWorkflowExecutionId` and the reserved structured `Result` output contract carrying child ID, terminal status, JSON-safe typed/redacted outputs, and safe diagnostic metadata, plus explicit outcomes `Dispatched`, `Completed`, `Faulted`, `Cancelled`, and `DispatchFailed`; this slice leaves `Result` unset and emits `Dispatched` only.
- **FR-004**: `WorkflowDefinitionId` MUST use existing backend-provided activity-input options and generic dropdown editor metadata, with no Studio-specific implementation.
- **FR-005**: The options provider MUST return only workflow definitions accessible to the author that currently have a live Published artifact.
- **FR-006**: Parent publication MUST resolve the selected definition to its current live Published artifact and minimally pin that artifact identity into the immutable parent executable.
- **FR-007**: Runtime dispatch MUST execute the pinned artifact rather than resolving the definition’s current publication again.
- **FR-008**: `Inputs` MUST populate only the child workflow-input channel and MUST NOT override variables, stimulus, reserved runtime metadata, or execution context; declared-name/value validation is completed by #677.
- **FR-009**: The reserved child workflow execution ID MUST be deterministic from the parent workflow execution identity and dispatch activity execution identity.
- **FR-010**: One fire-and-forget parent checkpoint MUST atomically persist activity completion, the `Dispatched` outcome, the reserved child ID output, one Pending dispatch lifecycle record, and one child-start post-commit intent.
- **FR-011**: Parent execution MUST be able to continue immediately after that durable checkpoint without waiting for transport acceptance or child execution-state materialization.
- **FR-012**: Re-delivery for one activity execution MUST converge on one dispatch record, one child identity, and one start intent; distinct activity executions MUST receive distinct identities.
- **FR-013**: The child-start intent handler MUST be contributed through the runtime post-commit handler mechanism and MUST invoke the existing workflow start dispatcher from the global resumption path outside workflow actor mailboxes.
- **FR-014**: Child start MUST use the configured workflow execution actor provider and MUST introduce no activity-level transport selector or broker abstraction.
- **FR-015**: Correlation MUST inherit from the parent unless a non-empty override is supplied.
- **FR-016**: Tenant or partition, execution-authority snapshot, run kind, parent-as-system identity, and root-initiator audit provenance MUST follow the parent contract into the child start request.
- **FR-017**: A first-class dispatch record MUST retain parent execution, parent activity execution, child execution, pinned child artifact, mode, lifecycle status, correlation, boundary/provenance metadata, and safe creation/update timestamps without retaining raw input values in its operational projection.
- **FR-018**: Detached dispatch records MUST remain available to track later child lifecycle even after the parent continues; terminal projection updates land in later lifecycle slices.
- **FR-019**: The implementation MUST include an in-memory end-to-end test proving asynchronous parent and child execution and explicitly documenting that process-crash durability is not claimed.
- **FR-020**: The module MUST declare its composition dependency on runtime resumption/background processing.
- **FR-021**: The implementation MUST add no MassTransit or other broker dependency, no Studio implementation dependency, and no reference to or change of the construct-only workflow-definition activity.

### Key Entities

- **Dispatch activity contract**: The authored static child target, declared inputs, mode/cancellation flags, correlation override, outputs, and explicit outcomes.
- **Pinned child artifact reference**: The exact live Published child executable selected during parent publication and carried immutably into runtime behavior.
- **Workflow dispatch record**: Durable parent/activity/child linkage and lifecycle state, initially `Pending` for a committed start responsibility.
- **Child-start intent**: Committed cross-execution work that asks the runtime to materialize the reserved child through the existing start dispatcher.
- **Reserved child workflow execution ID**: Deterministic identity derived from the parent execution and activity execution, stable across infrastructure redelivery.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Catalog and serialization tests discover exactly one `DispatchWorkflow` logical type with all five inputs, both outputs, required defaults, and generic dropdown metadata; activity contract tests expose all five explicit outcome constants.
- **SC-002**: Option-provider tests expose zero inaccessible or unpublished definitions and every returned definition has one live Published artifact visible to the author.
- **SC-003**: Publication tests prove the parent executable carries the exact selected Published child artifact identity.
- **SC-004**: Before child delivery runs, an integration test observes one committed Pending dispatch record and start intent, a completed parent activity with `Dispatched`, an available child ID, advancing parent work, and no materialized child execution.
- **SC-005**: Replaying one activity execution at least twice yields one logical dispatch and child; two distinct activity executions yield two distinct dispatch and child identities.
- **SC-006**: The in-memory end-to-end test starts and executes exactly one child through the existing start/actor path while the parent’s continuation is independent of child startup latency.
- **SC-007**: Correlation, tenant/partition, authority, run kind, system identity, and audit provenance assertions all match the parent contract or explicit correlation override.
- **SC-008**: Architecture and dependency audits report no broker, Studio implementation, or construct-only workflow-definition activity dependency or modification.

## Assumptions

- #675’s contributed post-commit handler seam and unfiltered global resumption delivery are the authoritative delivery path.
- The current workflow publication/executable compiler can be extended with a minimal pinned child artifact reference; input validation, behavioral hashing, transitive retention, recursion, and depth hardening are completed by #677.
- The current runtime start dispatcher accepts caller-reserved execution identity and inherited execution context, or this slice will minimally deepen that existing contract without adding a parallel start stack.
- The in-memory runtime provides semantic and asynchronous integration coverage only; Groundwork-backed restart durability and crash convergence belong to #678.
- The broader constitution remains draft/provisional. Accepted checkpoint and artifact decisions plus the current runtime contracts govern this work.

## Scope

### In Scope

- The dedicated activity runtime module and complete stable activity contract.
- Generic definition options limited to accessible live Published targets.
- Minimal publish-time child artifact pinning.
- Durable dispatch record and deterministic ID foundations.
- Fire-and-forget checkpoint staging and `Dispatched` continuation.
- Contributed child-start handler using the existing workflow start and actor-provider path.
- In-memory asynchronous end-to-end and focused architecture tests.

### Out of Scope

- Full publish-time input validation, dependency hashing/retention, and recursion/depth guards (#677).
- Groundwork restart durability, provider-backed crash convergence, and authenticated dispatch inspection (#678).
- Wait bookmark, successful child result capture, parent resume, and successful waited outcome (#679).
- Child fault and parent/child cancellation semantics (#680).
- Retry exhaustion, permanent failure/dead-letter handling, safe incidents, and authorized redrive (#681).
- Detached test-run scope expiry and teardown behavior (#682).
- Distributed two-node child-start routing and execution (#683).
- Adversarial authority/tenant rejection, malformed payload, and missing-artifact hardening beyond the inheritance and safe-failure plumbing required by this slice, where no current child issue explicitly assigns the check.
- Any change, integration, documentation, or test coverage for the construct-only workflow-definition activity.
- Broker-specific transport, Studio implementation code, and Elsa Core wire compatibility.
