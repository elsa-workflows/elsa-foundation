# Feature Specification: Step bodies change workflow state

**Feature Branch**: `agent/codex/3052-s13-engine-state`

**Created**: 2026-11-22

**Status**: Approved

**Input**: Complete ADR 0090's workflow-output, correlation-ID and instance-name capability for native activity bodies and imported Elsa 3 scripts, reusing the step transaction and keeping input expressions pure.

## Rule and boundary

This specification applies [ADR 0090 A-3](../../docs/adr/0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md), [ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md), [ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md), [ADR 0046](../../docs/adr/0046-output-binding-coercion-uses-pinned-value-representations.md), and [ADR 0093](../../docs/adr/0093-background-execution-eligibility-is-one-set-of-criteria.md). It extends the evaluation lifetime and commit preparation delivered by [spec 1231](../1231-step-variable-writes/spec.md). It introduces no second transaction, persistence mode or lost-update policy.

The delivered capability is for **inline leaf bodies**: a native leaf's invoke/resume and a Run JavaScript body. A successful suspension commits that evaluation; resume starts fresh. Structural evaluations retain their existing interfaces and have no new writable surface in this work unit. They remain separate transactions under ADR 0090; no transaction spans child execution. Background and closed graph/reusable bodies remain unsupported for the new capability, with explicit refusals.

Outputs mean the running workflow's named outputs, not an activity result, a reusable activity output, an input or a same-named variable. Correlation and instance name belong to the running workflow; its instance ID remains immutable. Native and imported bodies use one contract.

Excluded: graph intrinsic support and graph-variable writes; structural/background sessions; new joins; host interop; Liquid writes; `setInput`/`setResult`; arbitrary activity-output writes; a new capture target mechanism. Existing intrinsics and result/capture behavior remain. In particular, this does not deliver reusable-output return through a graph boundary. It replaces spec 1231 FR-034's temporary refusal for the three supported setters **in supported bodies only**; its other refusals remain.

## User Scenarios & Testing

### User Story 1 — Change workflow state as one step (Priority: P1)

An author sets an output, correlation and name alongside a variable and ordinary result, reads the pending state back, and trusts that failure saves none of that evaluation's successful effects.

**Independent test**: run native and script bodies writing all three state kinds plus a variable, returning their pending values. Repeat with fault, cancellation, invalid state, invalid outcome, and refused capture/variable commit.

**Acceptance scenarios**:

1. **Given** output `OrderTotal = 10`, correlation `old` and name `Draft`, **when** the body sets them to `15`, `new` and `Ready`, **then** its subsequent reads see those values, and later steps see all changes committed together with variables, result, outcomes and continuation.
2. **Given** repeated assignments to one address, **then** the last explicit assignment is its pending value. Reading or changing an unassigned copy alone writes nothing.
3. **Given** pending writes, **when** the body faults, is cancelled, produces an invalid completion or meets a refused commit, **then** no successful effects of that evaluation are accepted. Catching a host-side state refusal cannot make it commit.
4. **Given** output `OrderTotal` and variable `OrderTotal`, **then** a capture into the variable does not overwrite the workflow output: they are distinct addresses.

### User Story 2 — Preserve registered wait correlations (Priority: P1)

An author changes workflow correlation while another branch waits. The old wait keeps its registered key; later waits use their own evaluated inputs.

**Independent test**: keep a wait under `old`, commit correlation `new`, register a second wait explicitly bound to current workflow correlation, restart and deliver under each key.

**Acceptance scenarios**:

1. **Given** a registered `old` bookmark, **when** identity changes to `new`, **then** bookmark, registration and pending stimulus metadata retain `old`; `new` cannot resume it.
2. **Given** a later wait bound to current correlation, **then** it registers `new`. A wait with a different explicit input keeps that input. There is no new automatic inheritance.
3. **Given** a failed/cancelled correlation write, **then** neither workflow identity nor waits change. These results survive restart and work redelivery.

### User Story 3 — Import Elsa 3 body setters (Priority: P1)

An author imports scripts using `setOutput`, `setCorrelationId` and `setWorkflowInstanceName`, then publishes and runs native workflows with faithful evaluation order.

**Independent test**: import→publish→run all three setters, same-body identity reads, a variable write and a result; compare with a native body. Repeat throwing and refused cases.

**Acceptance scenarios**:

1. **Given** supported direct body calls, **then** conversion writes the intended workflow state in one evaluation, evaluating arguments once in source order.
2. **Given** a setter used as a value or last expression, **then** its no-result behavior is preserved; the assigned value becomes neither a result nor an outcome.
3. **Given** `setCorrelationId('new'); return getCorrelationId();`, or the corresponding instance-name pair, **then** the return reads pending `new`, not the pinned pre-step argument.
4. **Given** an expression-mode setter, unsupported alias/call shape, root collision or reusable write body, **then** conversion refuses with a stable code, source location and guidance. Locally declared functions with helper names keep their local meaning.
5. **Given** `setInput` or `setResult`, **then** conversion still refuses them.

### User Story 4 — Resume and recover consistently (Priority: P1)

An operator sees consistent workflow state after suspension, retry and recovery. Failed later evaluations do not undo earlier accepted work.

**Independent test**: exercise successful suspension/fresh resume, later resume fault, failure before checkpoint persistence and redelivery after it, using in-memory preparation tests and EF-backed recovery.

**Acceptance scenarios**:

1. **Given** a successful suspension with pending writes, **then** they commit at suspension. Resume starts without old pending intents and reads committed state.
2. **Given** a later failed evaluation, **then** only its changes are discarded; earlier accepted evaluations and child work stay committed.
3. **Given** failure before durability, **then** recovery follows the selected persistence mode for the whole checkpoint. After a durable commit, redelivery does not apply effects or successors twice.
4. **Given** output/identity-only writes and a refused capture, **then** recorded-completion acceptance cannot replay captures alone while omitting state effects.

### User Story 5 — Inspect authored and committed effects (Priority: P2)

An author sees a node's possible workflow-state effects. An operator sees committed outputs and identity, their responsible evaluation and any refusal without protected value exposure.

**Independent test**: inspect native/imported nodes and successful, failed, cancelled and protected instances in the composed Studio host; retain current-head screenshots and independent UX acceptance.

**Acceptance scenarios**:

1. **Given** known body writes, **then** the editor lists output targets and identity capabilities; computed/opaque access shows conservative effects rather than an empty list.
2. **Given** a successful evaluation, **then** existing instance views show committed outputs/current identity, and activity inspection attributes state kinds to the evaluation.
3. **Given** refusal/cancellation, **then** inspection shows that outcome without presenting attempted values as committed. Existing payload/capture policies withhold protected values.

### Edge cases

- Missing versus null output; empty, whitespace, case-distinct and prototype-like names; null/whitespace identity; `undefined`, wrong kind and nonserializable values.
- Multiple assignments, unassigned copy mutation, mixed variable/state writes, caught refusal and invalid completion/disposal/encoding/capture.
- Suspension/resume, retry, stale variable base, duplicate delivery and failure around the persistence marker.
- Old outstanding stimulus work after identity changes; later waits with explicit independent correlation.
- Protected sources feeding public identity, output names or diagnostics; helper/root shadowing, nested/short-circuit calls, spreads and aliases.
- Closed graph placement, structural evaluation and forged background execution bypassing publication.

## Requirements

### Functional Requirements

- **FR-001**: Inline native leaf and Run JavaScript bodies MUST explicitly assign/read running-workflow outputs, correlation and name through a narrow step capability; inputs, instance ID, services and arbitrary context members remain inaccessible through it.
- **FR-002**: Invoke/resume MUST have fresh pending state. Reads return committed working state plus own pending assignments as copies. Last explicit assignment wins within an evaluation; copy mutation alone writes nothing.
- **FR-003**: Valid completion or successful suspension MUST commit new state with existing variable writes, result/outcome acceptance and continuation work through one existing checkpoint. Fault, cancellation or any refused commit/projection MUST discard all successful effects of that evaluation. Host capability refusal remains fatal if caught by user code.
- **FR-004**: Calls MUST stage intents without directly persisting or mutating shared committed objects. External effects retain ADR 0045's rollback limits.
- **FR-005**: Existing variable bases, no-op behavior and lost-update checks MUST use their current commit rule. S-13 adds no conflict policy/version scheme for output/identity slots; serialized evaluations order successive assignments.
- **FR-006**: Preparation MUST combine identity/header and variable-frame updates without overwriting either. Step writes precede captures; a capture wins on the same addressed target only. Equal names in distinct namespaces are not overlaps. Inspection exposes real overlap; no new capture target is introduced.
- **FR-007**: Outputs MUST use the existing workflow-output channel and case-sensitive nonempty names, without a new declared-output-only restriction. Body assignments create a complete new dynamic JSON (`Any`) value with instance/inline storage by default, strengthened by source protection through the existing policy combiner. Replacing an output replaces its payload and type with Any while retaining the existing slot policy/storage as destination minima through the existing combiner. Source and destination protection cannot be weakened; incompatible or unavailable storage/encryption refuses the evaluation. JSON kinds/member casing survive. Null is an assignment; absent/undefined MUST NOT silently become null or delete an existing output.
- **FR-008**: Identity MUST accept string/explicit null and existing intrinsic normalization (whitespace becomes null). Output names reject null/empty/whitespace/non-string. Invalid kinds, unavailable or unserializable values refuse the evaluation with stable value-free diagnostics.
- **FR-009**: Changing/clearing correlation MUST leave existing bookmarks, registrations, pending stimulus metadata and captured correlation indexes unchanged. Later waits use their own inputs. Workflow header and expression identity projections change together.
- **FR-010**: Writable capability MUST be absent from input-expression, preflight and inspection evaluators. Writes remain refused at import/publication/runtime there. An argument object imitating a capability root grants no access.
- **FR-011**: Structural evaluations, background workers and closed graph/reusable bodies MUST receive no new session. Publication refuses declared capability use in those placements; execution enforces the boundary after authoring checks are bypassed. Graph-safe read-only scripts remain supported.
- **FR-012**: Native capability use MUST be declared/pinned in the contract and descriptor. Literal scripts expose known output/identity effects; computed output names or opaque access expose conservative effects. Studio shows them. Background eligibility recognizes the new capability independently of variable signals.
- **FR-013**: Direct Elsa 3 `setOutput(name,value)`, `setCorrelationId(value)` and `setWorkflowInstanceName(value)` in supported bodies MUST lower to the native capability, including computed nonempty output names resolved at runtime, using the same validation/commit path.
- **FR-014**: Lowering MUST preserve shadowing, order, short-circuit behavior, exactly-once argument evaluation and no-result setter values. Root collisions, opaque helper aliases, spread/wrong arity and unrepresentable values refuse with stable source-local guidance. Elsa 3 identity strings that native normalization would change MUST be refused, not silently normalized: reject literal empty/whitespace strings at import and guard computed values at execution with a sticky refusal. Nonblank strings and explicit null retain their value; the guard evaluates its argument once.
- **FR-015**: Same-body correlation/name getters MUST lower to pending-state reads. Input-expression identity reads remain pinned/pure. Other argument snapshots MUST NOT be silently refreshed.
- **FR-016**: `setInput`, `setResult`, expression setters and unsupported reusable state writes MUST retain stable refusal/guidance. A reusable output MUST NOT silently become its parent's workflow output.
- **FR-017**: State MUST retain existing output identities/identity projections and selected persistence mode/checkpoint replay marker without per-setter flushes. EF recovery proves failure before persistence, redelivery after persistence and consistent output/identity readback.
- **FR-018**: Failed later resumes MUST preserve prior successful evaluations. Recorded completions MUST carry all state effects or be ineligible for partial acceptance, including output/identity-only writers.
- **FR-019**: Protected/withheld values MUST follow existing policies on reads, encoding and inspection. The capability MUST NOT declassify protected sources into public correlation/name fields, output names or diagnostic text. Refuse such identity/name flows unless an existing explicit policy permits them; this introduces no new declassification policy.
- **FR-020**: Existing store/API/inspection MUST show committed state consistently and attribute state effects to invocation/attempt, distinguishing failure/cancellation. Attempted values and payload access obey existing permissions/capture policy; no new public value-history store is introduced.
- **FR-021**: The implementation PR MUST deliver required Studio authoring/inspection together, including empty, failed, cancelled and protected states. Retain composed-host interaction evidence and independent UX acceptance at the reviewed head; keep missing/failed acceptance visible.
- **FR-022**: Delivery evidence MUST cover import→publish→run, mixed-state rollback, stale-base/capture-conflict controls, old-bookmark/new-wait routing after restart, scope bypass and recorded-completion recovery. Converter/unit passes alone are insufficient.

### Key Entities

- **Evaluation**: one inline leaf invoke/resume with existing invocation/attempt identity and fresh pending intents.
- **State address**: workflow output name, correlation or instance-name slot, distinct from variables and activity results.
- **State intent**: validated explicit assignment provisional until the existing evaluation checkpoint accepts it.
- **State effect receipt**: value-free attribution to an evaluation/checkpoint; payloads remain in existing policy-controlled channels.
- **Captured wait correlation**: a wait's own registered key, independent of later workflow correlation.

## Success Criteria

- **SC-001**: Native/imported examples of all three state kinds publish/run, return same-body assigned values and expose identical committed outcomes.
- **SC-002**: Every fault/cancel/refusal control leaves zero successful effects of that evaluation, including caught host refusal and variable/capture conflict.
- **SC-003**: After change/restart, `old`/`new` deliveries resume only waits registered under those keys; zero existing bookmarks change their captured key.
- **SC-004**: Every durable-checkpoint redelivery control produces one effective accepted state transition/successor dispatch; rejected capture recovery never creates partial completion.
- **SC-005**: Independent review can identify authored effects and committed/failed states in the composed Studio journeys, with zero remaining verified UX findings or protected value exposure.

## Dependencies and assumptions

Spec 1231's leaf session integration, spec 1221's variable lifecycle and spec 1208's result/outcome contract are the baseline. Their placement boundaries stay explicit. ADR 0090 A-3 is accepted; implementation authorization and the scoped delivery sequence are recorded in the proposal forum. This document grants no new architecture approval. Existing intrinsic normalization, precision/storage policies and bookmark behavior are reused. Alternatives are those in ADR 0090; inspected extension paths/design choices are in [research](research.md). Live approval/questions belong on the proposal PR.
