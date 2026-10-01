# Feature Specification: Explicit Private Environment Inputs

**Feature Branch**: `codex/2292-explicit-environment-inputs`

**Created**: 2026-10-01

**Status**: Approved — #2277 authoring and #2282 correction delivery passed; implementation is active under #2292

**Input**: User description: Define a bounded first lane for explicitly supplied, private intended environment inputs during composition inspection while preserving candidate v1 and file-only compatibility.

This specification defines the actor-visible behavior and proof obligations for an explicit environment-input lane. Exact input grammar, numerical limits and protocol choices are recorded in the [reviewed plan](plan.md) and its contracts; source and integration constraints are linked in the planning handoff notes below.

## User Scenarios & Testing

### User Story 1 - Inspect an explicitly supplied intended environment (Priority: P1)

As a developer or operator reviewing a selected host, I want to supply the intended environment values for one invocation so that the inspection reflects the environment I chose, without reading the caller's ambient environment or exposing private values.

**Why this priority**: An inspection that silently uses the caller's process environment cannot be reproduced or trusted as a review of the intended host configuration.

**Independent Test**: Supply a valid accepted file-only composition, a selected host/shell/environment/invocation identity, and a private explicitly supplied overlay document. The inspection can be reviewed for its selected context, resolved targets, refusals, and redaction without requiring a database, host startup, package acquisition, or caller ambient input.

**Acceptance Scenarios**:

1. **Given** an accepted file-only composition and a supported Workbench invocation, **when** a complete explicitly supplied overlay document is provided, **then** the inspection uses that document for the selected host, shell, environment, and invocation and reports only safe logical results and evidence status.
2. **Given** an explicit key whose value is an empty string, **when** the overlay is inspected, **then** the empty value is treated as an intentional value and is not treated as omission or removal.
3. **Given** a key omitted from the explicit overlay, **when** the same composition is inspected, **then** the omitted key contributes no external entry and lower declared sources retain their normal effect; the caller's ambient value is not imported.

### User Story 2 - Reconcile environment-driven selection before preparation (Priority: P1)

As a developer reviewing a composition, I want a selection difference caused by the supplied environment to be identified before preparation so that I can correct the accepted intent through the existing acceptance workflow instead of receiving a misleading prepared result.

**Why this priority**: Feature selection and persistence preparation must describe one coherent accepted input. A result prepared from a different selection is not a trustworthy inspection.

**Independent Test**: Supply an overlay document that changes the effective feature selection or removes an accepted feature. The inspection refuses before preparation and identifies the reconciliation problem without accepting it silently. Edit the authored selection intent, run the existing `composition accept` workflow to publish a fresh accepted file, and start a new inspection invocation; a valid matching selection can then proceed.

**Acceptance Scenarios**:

1. **Given** accepted feature IDs and an explicit environment that changes the effective selection, **when** inspection begins, **then** it refuses the mismatch before persistence preparation and does not silently rewrite the accepted file.
2. **Given** a refused selection mismatch, **when** the developer edits the authored selection intent and uses the existing interactive `composition accept` workflow, **then** a fresh accepted file is produced and a later inspection uses that fresh accepted input and capture.
3. **Given** a selection that reconciles successfully, **when** persistence preparation runs, **then** the inspection uses the same host-owned configuration policy and captured overlay for that selected context.

### User Story 3 - Preserve compatibility and private review boundaries (Priority: P1)

As a maintainer, I want existing candidate v1 and file-only behavior to remain compatible while unsupported hosts and unsafe inputs refuse explicitly, so that the new lane can be proven without turning existing clients into ambient configuration readers.

**Why this priority**: Compatibility and privacy are release boundaries for composition inspection. An opt-in capability must not change the meaning of existing candidate requests.

**Independent Test**: Exercise existing candidate v1/file-only input and the new negotiated explicit-input lane through the supported inspection flow. Verify unchanged v1 behavior, explicit refusal for unsupported capabilities and malformed input, preservation of supplied input files, and absence of private canaries from output, arguments, logs, diagnostics, or generated/public artifacts.

**Acceptance Scenarios**:

1. **Given** an existing candidate v1/file-only request, **when** it is handled by a host that does not enroll in the new lane, **then** its existing behavior remains unchanged and no ambient environment is copied into it.
2. **Given** a host or caller that does not negotiate the explicit-input capability, **when** it receives a request for that lane, **then** the request is refused without fallback to caller ambient values or an unverified source.
3. **Given** a valid explicit-input inspection whose operator-owned supplied input contains private canary values, **when** the inspection completes, **then** the original input remains unchanged, no extra persisted copy is created, and the canaries never appear in public results, process arguments, logs, diagnostics, or generated/public artifacts.

### Edge Cases

- A JSON `null`, tombstone, or removal operation is refused; an empty string is valid; omission contributes no external entry. The first lane must not invent a delete operation whose precedence is unclear. Valid authored feature removals remain supported and must survive reconciliation.
- Raw keys that differ only by case, by `__` versus `:`, or by another provider alias must be admitted and normalized deterministically, or refused before lossy normalization. The ordinary normalized `ConnectionStrings__<name>` form is supported as a normal key. Standard service-connection prefix expansions are explicitly refused in this lane; support is deferred follow-up work and cannot be added silently during planning.
- A duplicate raw key, normalized collision, unsupported control character, over-limit key/value, malformed capture, or unsupported source shape is refused before any private value reaches a public projection. Exact grammar and bounds are defined in the planning contract.
- A changed source file, mixed capture, stale binding identity, cancellation, or timeout refuses the inspection without a partial result or later reuse of a stale private capture.
- An external feature-selection change that diverges from accepted IDs follows the existing edit-and-accept recovery path. Inspection does not assume that `composition accept` can observe external environment changes automatically.
- Selected-host inspection executes trusted selected host code. The lane provides no general sandbox; trust of selected host code is explicit and does not authorize runtime activation.
- A self-declared intended environment is not deployed attestation, physical readiness, database reachability, migration authorization, or activation evidence. Those claims remain unavailable unless a separate evidence source proves them.
- The first lane is enrolled only for the reviewed Workbench host policy. Foundation Host, command-line providers, custom providers, and caller ambient environment remain outside the lane until their policies are separately established.

## Requirements

### Functional Requirements

- **FR-001**: The feature MUST be explicitly opted into for one invocation and MUST bind the supplied intended environment to one selected host, shell, environment, and invocation identity.
- **FR-002**: The feature MUST consume the explicitly supplied intended environment as a private input and MUST NOT copy caller ambient environment, command-line values, other caller-supplied sources, deployed observations, or physical readiness claims into the lane.
- **FR-003**: The inspection MUST privately capture one complete explicitly supplied overlay document for the invocation, associate it with the accepted composition and selected context, and use that captured input consistently throughout inspection. It MUST NOT reread or mix a different generation of supplied input.
- **FR-004**: The enrolled Workbench host policy MUST preserve its reviewed source ordering and precedence. The first lane MUST NOT imply enrollment for another host whose source policy has not been proven.
- **FR-005**: Candidate v1 and file-only requests MUST remain compatible. The explicit-input lane MUST be negotiated as a separate capability and MUST refuse unsupported versions, shapes, or hosts rather than silently falling back to ambient inputs or changing candidate v1 semantics.
- **FR-006**: Raw-key admission and normalization MUST be deterministic and MUST detect duplicate or normalized-colliding entries before any lossy normalization. The planning contract defines exact supported character grammar and bounds without imposing strict ASCII.
- **FR-007**: Empty-string values MUST remain intentional values. JSON `null`, tombstone, and removal forms MUST be refused in the first lane. An omitted key MUST contribute no external entry and MUST NOT delete or mask a lower declared source.
- **FR-008**: Ordinary normalized `ConnectionStrings__<name>` keys MUST follow the same supported-key rules as other normalized keys. Standard service-connection prefix expansions MUST be refused in the first lane. Supporting those expansions is deferred follow-up work and MUST NOT be added silently during planning.
- **FR-009**: The host MUST reconcile accepted, requested, effective, and disabled feature selections before persistence preparation. Valid authored feature removals MUST be preserved. Divergence between the sets, a removed feature remaining active contrary to accepted intent, a required-edge conflict, or a stale accepted identity MUST refuse before preparation and MUST NOT silently rewrite accepted intent.
- **FR-010**: A valid recovery path MUST use the existing authored-edit and interactive `composition accept` workflow to publish a fresh accepted file, followed by a fresh inspection capture. The feature MUST NOT claim that acceptance can silently observe or authorize an external environment change.
- **FR-011**: After successful reconciliation, the inspection MUST use the same host-owned configuration policy for the selected context and the same captured input. It MUST NOT present a result assembled from a competing or independently reread policy.
- **FR-012**: The inspection MUST distinguish resolved, refused, unavailable, and unverified evidence. It MUST identify the supplied environment as intended input and MUST NOT present it as deployed attestation, physical readiness, migration authorization, package reachability, connectivity, schema state, or activation evidence.
- **FR-013**: Public results, logs, process arguments, generated/public artifacts, and diagnostics MUST exclude captured private environment values, raw configuration, unknown values, secret-bearing excerpts, private-input paths, and all fingerprints derived from secret-bearing or private inputs. Existing explicit CLI file-location and required assembly-loader paths MAY identify local inputs where needed, but MUST NOT be emitted in public results or diagnostics. Operator-owned supplied input files MUST be preserved and MUST NOT be copied into additional persisted artifacts. Public output MAY contain only declared public logical identities, fixed provider/selection metadata, redacted metadata, and truthful evidence status. Modeled resource/connection references are public labels; private connection material and arbitrary/non-modeled overlay values remain excluded. Unknown overlay-only identities MUST NOT be echoed in errors merely because their spelling is syntactically safe.
- **FR-014**: The inspection MUST refuse unsupported capability versions, malformed or over-limit supplied input, stale binding, and changed input without a legacy fallback or partial public result.
- **FR-015**: Selected-host inspection MUST treat selected host code as explicitly trusted. The lane MUST make no general sandbox promise.
- **FR-016**: The feature MUST not acquire packages, start the host runtime, activate services, access a database, run migrations, publish, activate, or save application state as part of inspection. Cancellation and timeout MUST clean up the inspection and private capture state and MUST not emit raw console output.
- **FR-017**: Repeating an inspection with identical supported inputs and the same accepted context MUST produce the same safe logical result and refusal classification. Source drift, mixed capture, or changed binding MUST refuse rather than mix generations.

### Key Entities

- **Intended Environment Overlay**: The complete explicitly supplied overlay document for one invocation, including its selected host/shell/environment/invocation binding and private values. It is not a full deployed, ambient, or already-effective environment.
- **Selected Inspection Context**: The accepted composition and host context against which the intended overlay is interpreted.
- **Accepted Candidate Intent**: The file-only accepted feature and configuration intent that the developer has explicitly accepted before inspection.
- **Selection Reconciliation**: The comparison of accepted, requested, effective, and disabled selections performed before preparation.
- **Configuration Resolution Projection**: A safe view of logical targets, source family, and evidence state produced by the host-owned resolution path without private values.
- **Inspection Outcome**: A deterministic result classified as resolved, refused, unavailable, or unverified, with safe reasons and no private payload.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every supported Workbench scenario with one accepted composition, selected context, and explicit overlay produces a safe result from that same supplied input; no scenario succeeds by reading caller ambient environment.
- **SC-002**: Existing candidate v1/file-only compatibility scenarios retain their behavior, while unsupported explicit-input capabilities refuse explicitly without ambient fallback.
- **SC-003**: All privacy-canary scenarios show zero private environment values or derived private-input fingerprints in public results, logs, process arguments, diagnostics, and generated/public artifacts; supplied operator-owned input files remain unchanged and are not copied.
- **SC-004**: Supported input classes produce stable, distinct outcomes for empty values, omitted entries, explicit null or delete/tombstone forms, key aliases and collisions, ordinary `ConnectionStrings__` entries, refused service-prefix forms, duplicate entries, and over-limit input.
- **SC-005**: Selection divergence is refused before preparation, and at least one recovery scenario shows that editing intent, running existing `composition accept`, and starting a fresh inspection can produce a valid matching result.
- **SC-006**: Changed supplied files, mixed or stale input, cancellation, timeout, malformed input, and unsupported host scenarios produce no partial public result and no side effect listed in FR-016.
- **SC-007**: Successful outputs label intended input and unavailable evidence truthfully; no successful scenario claims deployed attestation, physical readiness, migration authorization, connectivity, schema state, or activation.

## Assumptions

- The first enrolled host is the reviewed Workbench policy. Its source ordering is preserved from the existing host composition; another host requires an independently proven policy before enrollment.
- Existing candidate v1 and file-only contracts remain closed and compatible. The new lane uses capability/version negotiation rather than changing those contracts.
- Existing composition capture and host configuration policy provide the proof basis. Detailed source and integration constraints are recorded in the planning handoff notes.
- The actor requirements delegate exact numerical limits, envelope shape, capability name/version and raw-key grammar to the reviewed planning contract.
- The intended overlay is supplied by the caller as an explicit input and is not evidence of what is deployed or physically available. Privacy is enforced through private capture, scope, and output boundaries; no memory-erasure guarantee is implied.
- The existing `composition accept` workflow is the recovery mechanism for accepted-selection divergence. No new acceptance command or silent acceptance behavior is assumed.
- Ordinary normalized `ConnectionStrings__<name>` keys are in scope. Standard service-connection prefix expansion is explicitly refused in the first lane and deferred to separately scoped follow-up work.
- Configuration taxonomy and broader host enrollment remain deferred where the constitutions mark them provisional or deferred. This specification does not ratify a general configuration model.

### Planning Resolution

- [Research](research.md) and the [environment-input contract](contracts/environment-input-v1.md) settle additive capability negotiation, private transport, supported key/value grammar, collision policy, ownership and bounds while preserving candidate v1.
- The [CLI contract](contracts/cli-inspect-environment-v1.md) defines opt-in inspection, truthful source-family evidence, refusal classes and existing edit-and-accept recovery.
- The [proof matrix](contracts/acceptance-proof-matrix.md) assigns all requirements and outcomes to actual public-wrapper/Workbench, same-capture preparation, compatibility, lifecycle and privacy cases. Every case remains planned until implementation.
- Service-prefix expansion remains separately scoped follow-up work; first-lane planning does not add it.

### Planning Handoff Notes

The actor-facing requirements above are bounded by existing source and integration evidence. Planning should use the [external-environment discovery report](../../docs/reports/runtime-composition/external-environment-inputs.md#required-next-contract), [Spec 187](../187-effective-persistence-preview/spec.md#requirements), the [candidate v1 contract](../187-effective-persistence-preview/contracts/candidate-inspection-v1.md), and the [edited-selection acceptance report](../../docs/reports/runtime-composition/edited-selection-acceptance.md) to select the capability/version, input shape, exact limits, capture checks, host source ordering, redaction proof, and recovery cases. Existing source budgets and the established proof matrix remain planning constraints. Those are implementation planning constraints, not additional actor requirements. The existing candidate v1/file-only path, Workbench enrollment boundary, trusted selected-host execution, host-owned configuration policy, and explicit acceptance recovery remain compatibility constraints for that planning work.
