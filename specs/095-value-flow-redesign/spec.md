# Feature Specification: Replace Memory-Block Value Flow

**Feature Branch**: `598-value-flow-redesign`

**Created**: 2026-07-16

**Status**: Draft

**Input**: Replace the Elsa 3 memory-block activity/value model with role-specific bindings,
invocation-owned input and result records, scoped variables, typed activity state and triggers, and a
pleasant code-first workflow authoring experience that compiles to the same canonical artifact as
dynamic authoring.

**Decision**: [ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)

**Terminology**: [Elsa glossary](../../docs/glossary/elsa.md)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author transient CLR activities with ordinary inputs and typed results (Priority: P1)

An activity author defines a transient CLR activity whose constructor receives services, whose
annotated properties receive workflow data, and whose successful completion returns one typed result.
The author does not create or manipulate memory blocks, value addresses, or argument wrappers inside
the activity.

**Why this priority**: This is the primary replacement for the accidentally ported Elsa 3 activity
programming model and the foundation for constructor injection.

**Independent Test**: Define and invoke an activity with one injected service, required and defaulted
inputs, a typed result, and two named control outcomes; verify one-time input hydration, typed result
projection, service disposal, and the absence of memory APIs from the activity contract.

**Acceptance Scenarios**:

1. **Given** an activity with constructor services and required inputs, **When** an invocation begins,
   **Then** the runtime creates a fresh activity object, supplies services through construction, and
   hydrates all input properties before activity code runs.
2. **Given** an activity that completes successfully, **When** its result is committed, **Then** the
   whole typed result becomes available atomically and named outputs are read-only projections from
   that result.
3. **Given** a retry or resumption of one logical invocation, **When** the activity runs again, **Then**
   it receives a fresh CLR object and attempt identity while retaining its logical invocation identity
   and pinned inputs.
4. **Given** an activity contract with stable member keys, **When** CLR members are renamed without
   changing those keys, **Then** an existing compatible executable remains valid against the pinned
   contract version.

---

### User Story 2 - Compose typed code-first workflows with method-call syntax (Priority: P1)

A workflow developer inherits from an IDE-discoverable typed workflow-definition base, overrides its
build method, and composes activities using generated named-argument method calls. Literals, workflow
request members, variables, prior results, and expressions can be supplied naturally without exposing
runtime binding machinery.

**Why this priority**: Code-first composition is a first-class developer experience, not merely a
reflection fallback or a second execution model.

**Independent Test**: Build a typed request-to-result workflow using generated activity calls,
variables, a conditional scope, and a child workflow; compile it and compare its canonical artifact
with an equivalent dynamically authored workflow.

**Acceptance Scenarios**:

1. **Given** a generated activity call, **When** a developer supplies literals and typed dynamic
   sources as named arguments, **Then** the call is accepted without explicit memory or runtime
   reference objects.
2. **Given** a generated call handle, **When** the developer inspects it through IDE completion,
   **Then** node identity, whole result, named result projections, and control outcomes are clearly
   separated.
3. **Given** a typed workflow definition, **When** it is compiled or published, **Then** its build
   method executes once for that definition version and does not execute when individual workflow
   instances start.
4. **Given** equivalent code-first and dynamic definitions, **When** both are compiled, **Then** they
   produce semantically equivalent canonical bindings and receive identical validation.
5. **Given** generated authoring conveniences, **When** the artifact is serialized, **Then** no
   generated authoring carrier, CLR delegate, or captured closure appears in the artifact.

---

### User Story 3 - Preserve invocation values across durable execution (Priority: P1)

A workflow operator can suspend, resume, retry, and move execution between workers without activity
inputs changing or successfully completed results being recomputed.

**Why this priority**: Stable invocation values are the core durability guarantee that replaces a
live memory register.

**Independent Test**: Start an activity from a variable and expression, suspend it, mutate the source
variable, restart the worker, and resume; verify the activity sees its original input snapshot and
committed state. Separately interrupt execution after result commit and verify downstream work
continues without re-invoking the completed activity.

**Acceptance Scenarios**:

1. **Given** a durable activity invocation, **When** its inputs are materialized, **Then** its logical
   identity and complete input snapshot are committed before activity construction or user code.
2. **Given** a suspended invocation whose source variable later changes, **When** the invocation
   resumes, **Then** it observes the original pinned input rather than reevaluating the binding.
3. **Given** a completed invocation whose downstream scheduling is interrupted, **When** recovery
   continues, **Then** the committed result is reused and the activity is not executed again.
4. **Given** a nonpersistable input, state, or result in durable execution, **When** materialization or
   completion is attempted, **Then** execution is rejected before an unsafe durability boundary is
   crossed.
5. **Given** explicitly transient execution, **When** a nonpersistable value is used, **Then** the
   invocation cannot suspend, migrate, or claim durable retry semantics.

---

### User Story 4 - Move data through lexical scopes without ambiguous shared state (Priority: P2)

A workflow developer declares local variables, explicitly assigns them, returns typed values from
structured scopes, and receives deterministic validation for parallel branches, loops, and cycles.

**Why this priority**: Variables and result flow must remain understandable once memory addresses and
global latest-output lookup are removed.

**Independent Test**: Compose nested sequences, conditionals, parallel branches, a parallel loop, and
a cyclic flowchart; verify lexical visibility, per-iteration frames, deterministic result order,
concurrent-write diagnostics, and explicit back-edge state transfer.

**Acceptance Scenarios**:

1. **Given** a variable declared in a structured scope, **When** that scope activates repeatedly or in
   parallel, **Then** each activation receives its own variable frame and descendants resolve the
   correct declaring frame.
2. **Given** two branches that may write the same outer variable, **When** the workflow is validated,
   **Then** ordinary concurrent writes are rejected unless an explicit deterministic merge governs
   them.
3. **Given** a result produced inside a nested scope, **When** an outer consumer needs it, **Then** the
   scope must explicitly return, collect, reduce, or merge that result.
4. **Given** a required result binding, **When** its producer is absent on any path to the consumer,
   **Then** publication rejects the binding rather than supplying `null`, a default, or a stale value.
5. **Given** repeated execution of one node in a cycle, **When** a later invocation needs prior data,
   **Then** it uses explicit state transfer and cannot request the globally latest node result.
6. **Given** parallel loop iterations that finish out of order, **When** their results are collected,
   **Then** collection order follows stable input/iteration identity rather than completion timing.

---

### User Story 5 - Evaluate portable expressions with explicit dependencies (Priority: P2)

A workflow author uses JavaScript, Liquid, or another registered expression language as an input
binding while keeping all value dependencies explicit and the expression free of workflow-state side
effects.

**Why this priority**: String-based expression languages remain essential, but ambient memory access
would undermine the new value model.

**Independent Test**: Bind the same declared parameters to JavaScript and Liquid expressions, round
trip their definitions, evaluate them after restart, and verify undeclared value access and variable
mutation are rejected.

**Acceptance Scenarios**:

1. **Given** a string expression with explicit named parameters, **When** the workflow is published,
   **Then** the artifact records the language, source, result type, parameters, and options as portable
   data.
2. **Given** an expression that attempts ambient variable/output access or mutation, **When** it is
   validated or evaluated, **Then** it fails with a precise purity/capability diagnostic.
3. **Given** a stateful script, **When** it must affect workflow state, **Then** it runs as an activity,
   returns a typed result, and any variable assignment remains a separate explicit workflow operation.
4. **Given** time, randomness, or another nondeterministic dependency, **When** it participates in an
   expression, **Then** it is supplied as an explicit pinned input or a separately approved
   deterministic capability.

---

### User Story 6 - Suspend and resume with typed private state and triggers (Priority: P2)

An activity author can suspend an invocation with one immutable private state document and resume a
fresh activation with that committed state plus a typed trigger payload.

**Why this priority**: Long-running activities need durable private state without relying on CLR
fields, memory blocks, or a string-keyed state bag.

**Independent Test**: Suspend an activity, dispose its activation and service scope, reload on another
worker, deliver the same trigger twice, and verify fresh activation, stable state, typed payload,
deduplication, and one successful completion.

**Acceptance Scenarios**:

1. **Given** a stateful activity's initial execution, **When** it suspends, **Then** bookmark/trigger
   registration and its complete immutable state document are committed atomically.
2. **Given** a resumed activity, **When** the runtime activates it, **Then** it receives pinned inputs,
   the last committed state, and one expected typed trigger payload; no CLR field or injected service
   survives from the prior attempt.
3. **Given** an unexpected or duplicate trigger, **When** delivery is attempted, **Then** it is rejected
   or deduplicated before user activity code observes an invalid transition.
4. **Given** an activity fault, cancellation, suspension, or successful completion, **When** its
   transition is recorded, **Then** those cases remain distinct and no hidden variable mutation is
   included in the transition.

---

### User Story 7 - Import Elsa 3 workflows without retaining memory semantics (Priority: P3)

A migration operator imports an Elsa 3 workflow whose inputs, outputs, variables, and expressions use
memory references. The importer either translates each reference into a canonical Elsa 4 value role
or reports an actionable incompatibility.

**Why this priority**: Compatibility is required at the migration boundary, but it must not dictate
the canonical Elsa 4 runtime.

**Independent Test**: Import representative Elsa 3 workflows covering literals, variables, activity
outputs, JavaScript/Liquid, loops, and unsupported custom memory references; verify successful cases
contain no memory interfaces and unsupported cases carry precise diagnostics.

**Acceptance Scenarios**:

1. **Given** a representable Elsa 3 memory reference, **When** it is imported, **Then** it becomes an
   Elsa 4 literal, workflow input, variable read, result projection, expression, or explicit state
   transfer as appropriate.
2. **Given** an unrepresentable custom memory construct, **When** it is imported, **Then** import fails
   that construct with a precise diagnostic rather than adding a memory compatibility path to the
   canonical runtime.
3. **Given** canonical Elsa 4 packages, **When** their public and internal dependency surfaces are
   inspected after migration, **Then** they do not expose the legacy memory interfaces or importer
   DTOs.
4. **Given** an Elsa 3 output-only memory reference or a value containing both an expression and a
   memory reference, **When** it is imported, **Then** the importer resolves its producer/consumer
   relationship or emits a path-specific diagnostic; it never drops the reference silently.

---

### User Story 8 - Choose activity DI isolation from measured evidence (Priority: P3)

An architect can choose the activity activation scope contract using reproducible performance and
isolation evidence rather than assumption.

**Why this priority**: Per-attempt isolation is attractive, while burst-scope reuse may materially
affect micro-activity throughput. The decision must preserve correctness and disposal semantics.

**Independent Test**: Run the agreed activity activation workloads under burst-only, per-attempt
child-scope, and safe conditional strategies; compare throughput, latency, allocations, disposal,
service isolation, retry, and resumption behavior.

**Acceptance Scenarios**:

1. **Given** the three candidate activation strategies, **When** the benchmark suite runs, **Then** it
   reports comparable throughput, latency, and allocation measurements for every agreed workload.
2. **Given** scoped and disposable dependencies, **When** activities execute sequentially, retry, and
   resume, **Then** the benchmark also verifies observable isolation and disposal behavior.
3. **Given** engine-intrinsic control and value operations, **When** they execute, **Then** they create
   no CLR activity activation or activity DI scope.
4. **Given** a proposed conditional fast path, **When** dependency graphs and service access are
   inspected, **Then** the strategy is rejected unless it can preserve the chosen observable lifetime
   semantics for transitive dependencies.

### Edge Cases

- An omitted generated argument is distinct from an explicit `null` and from a literal default value.
- A non-nullable input without a binding or pinned default is invalid.
- A workflow source changes after an invocation snapshot commits but before user code starts.
- A process crashes after an external side effect but before activity completion commits.
- A result member is sensitive while the destination contract has a weaker persistence policy.
- An output producer executes more than once in one structural scope or on converging control paths.
- A conditional branch completes successfully without producing its declared structured result.
- A loop retries one iteration after other iterations have completed.
- An expression language extension attempts to register ambient mutable host functions.
- An activity contract is present under the expected identifier but has a different schema
  fingerprint.
- Definition-time services produce different graphs for the same declared definition version.
- A child workflow suspends, outlives its parent, faults, or returns a nonpersistable result.
- A source or result type is a generated handle-like type and would make implicit authoring conversion
  ambiguous.
- An Elsa 3 reference targets a memory block outside the subtree being imported or copied.

## Requirements *(mandatory)*

### Functional Requirements

#### Canonical value roles and bindings

- **FR-001**: The canonical model MUST distinguish workflow requests, activity inputs, activity
  results, variables, activity-private state, resume triggers, and faults by their different lifetime
  and mutation semantics.
- **FR-002**: The canonical model MUST NOT expose a public runtime memory-block, memory-register, or
  universal value-reference contract.
- **FR-003**: Every published activity input MUST contain exactly one concrete binding to a literal,
  workflow-request member, variable read, causally available activity-result projection, or explicit
  expression definition.
- **FR-004**: Publication MUST distinguish omitted arguments, explicit `null`, and literal default
  values, and MUST normalize every valid input to a concrete binding before execution.
- **FR-005**: Generated code-first authoring carriers MUST be absent from serialized executables and
  runtime contracts.
- **FR-006**: Code-first, visual, and API/JSON authoring MUST converge on the same canonical executable model
  and validation rules.

#### Workflow and activity contracts

- **FR-007**: A code-first workflow MUST expose one typed immutable request contract and one typed
  immutable successful-result contract.
- **FR-008**: Workflow request values MUST be pinned when a workflow instance starts and MUST remain
  immutable for that instance.
- **FR-009**: Every successful workflow terminal path MUST produce the complete declared result;
  optional members MUST be explicitly represented by the result contract.
- **FR-010**: An activity MUST declare independently bindable input members and one atomic typed
  successful result.
- **FR-011**: Activity result members MUST be read-only projections from the atomic result and MUST NOT
  be independently writable output slots.
- **FR-012**: Published activity contracts MUST carry stable contract/member identities, a version,
  and a schema fingerprint sufficient to prevent incompatible activation.
- **FR-013**: A compatible CLR member rename MUST be possible without changing the stable member key;
  incompatible schema changes MUST require a new contract version.
- **FR-014**: Activity constructors MUST receive services rather than workflow data, and workflow data
  MUST be fully hydrated before user activity code runs.
- **FR-015**: Runtime activity contexts MUST NOT expose a general service-locator path that bypasses
  the chosen constructor-injection lifetime contract.
- **FR-016**: Authoritative input defaults MUST be captured in the immutable executable and MUST NOT be
  rediscovered by constructing activity objects during execution.
- **FR-017**: Generated, manually described, and reflection-discovered contracts MUST agree on input
  stable keys, requiredness, pinned defaults, editor metadata, result schema, and outcome schema.
- **FR-018**: Publication MUST prove that every activity contract and required activation capability
  pinned by an executable is available and compatible; a missing constructor or required service MUST
  NOT be an expected invocation outcome.

#### Invocation lifecycle and durability

- **FR-019**: Durable execution MUST commit a logical invocation identity and complete materialized
  input snapshot before constructing or invoking user activity code.
- **FR-020**: Retries and resumptions MUST retain one logical invocation identity and pinned input
  snapshot while using a distinct attempt identity and fresh activity activation.
- **FR-021**: Successful activity completion MUST commit one complete typed result and authored control
  outcome before downstream work is scheduled.
- **FR-022**: Recovery after a committed completion MUST reuse the completion record and MUST NOT
  reexecute the activity solely because downstream scheduling was interrupted.
- **FR-023**: Persistable fault information MUST be normalized into a safe runtime record; arbitrary
  CLR exception objects MUST NOT become ordinary persisted workflow values.
- **FR-024**: Nonpersistable input, state, or result values MUST be rejected for durable execution and
  MUST require an explicit transient policy.
- **FR-025**: Transient execution carrying nonpersistable values MUST NOT suspend, migrate between
  workers, or claim durable retry guarantees.
- **FR-026**: Invocation snapshots, activity state, completion results, and variable-frame changes MUST
  participate in the existing atomic checkpoint commit boundary and MUST NOT create a second
  persistence route.

#### Variables, scope, and causal result flow

- **FR-027**: Variable declarations MUST have lexical scope and runtime values MUST be isolated by one
  concrete activation frame of that scope.
- **FR-028**: Variable reads MUST resolve when the consumer invocation materializes its inputs.
- **FR-029**: Variable writes MUST be explicit graph-visible operations and MUST commit before
  sequential downstream work observes them.
- **FR-030**: Input-binding expressions, activity completion, and activity transitions MUST NOT
  perform hidden variable writes.
- **FR-031**: Potentially concurrent ordinary writes to the same variable MUST fail publication unless
  an explicit deterministic merge or reduction governs them.
- **FR-032**: A required activity-result binding MUST identify a producer that is causally and
  structurally available to the consumer on every execution path.
- **FR-033**: Runtime result resolution MUST use the consumer's structural frame and causal lineage and
  MUST NOT select a globally latest node execution.
- **FR-034**: Data crossing a structured-scope boundary MUST use an explicit typed return, collection,
  selection, merge, or reduction.
- **FR-035**: Cyclic back-edges MUST use explicit state transfer for iteration-to-iteration data.
- **FR-036**: Loop and parallel result collection MUST use stable input/branch identities and MUST NOT
  depend on completion timing.

#### Expressions, state, and triggers

- **FR-037**: A portable expression definition MUST carry a language, source, expected result type,
  explicit named parameter bindings, and evaluator options as serializable data.
- **FR-038**: An input-binding expression MUST be read-only, MUST NOT retain a delegate or captured
  closure, and MUST NOT discover workflow values through ambient name-based access.
- **FR-039**: Expression evaluators MUST withhold mutating workflow host functions and MUST require
  nondeterministic dependencies to be explicit or separately governed by a deterministic capability.
- **FR-040**: A script with workflow-visible side effects MUST execute as an activity and expose data
  through its typed result.
- **FR-041**: A stateful activity MUST persist one immutable private state document per logical
  invocation rather than relying on CLR fields or a string-keyed memory bag.
- **FR-042**: Initial and resumed activity execution MUST be distinguishable, and resumption MUST
  supply one expected typed trigger payload separately from pinned activity inputs.
- **FR-043**: Trigger deliveries MUST carry durable identity sufficient for duplicate detection.
- **FR-044**: Complete, suspend, fault, and cancel MUST remain distinct activity transitions, and the
  invocation contract MUST return the selected transition without publishing results, outcomes, or
  variable writes through mutable context methods.

#### Code-first developer experience

- **FR-045**: The recommended code-first workflow entry point MUST provide IDE-guided implementation of
  the typed build contract without requiring a memorized static method convention.
- **FR-046**: A workflow definition build MUST run only when compiling or publishing a definition
  version, never when starting an individual workflow instance.
- **FR-047**: Equivalent declared build inputs MUST produce the same behavioral artifact identity;
  definition-time configuration changes that alter behavior MUST produce a new artifact.
- **FR-048**: The foundational fluent API MUST distinguish dynamic sources from literal values without
  introducing a universal runtime value-source contract.
- **FR-049**: The generated code-first facade MUST support named activity method arguments sourced from
  literals, workflow request members, variables, activity results, and expressions.
- **FR-050**: Generated call handles MUST make node identity, whole result, named result projections,
  and control outcomes independently discoverable.
- **FR-051**: Child workflows MUST exchange values only through typed request and result contracts and
  MUST NOT share variables implicitly with their parent.
- **FR-052**: Ordinary language-level builder extension methods MUST support reusable source
  composition without requiring a separate fragment runtime abstraction.

#### Persistence policy and compatibility

- **FR-053**: Persistence, external-payload storage, encryption, sensitivity, and redaction policy MUST
  belong to the schema or variable declaration that owns the value.
- **FR-054**: Value flow MUST NOT silently downgrade a source value's required protection when it is
  projected, transformed, returned from a scope, or passed to a child workflow.
- **FR-055**: Elsa 3 memory references MUST be translated at the one-way importer boundary into
  canonical Elsa 4 value roles or produce precise migration diagnostics.
- **FR-056**: The importer MUST resolve output-only memory references and values containing both an
  expression and memory reference through an importer-local reference table; dangling, cross-subtree,
  ambiguous, and custom references MUST produce path-specific diagnostics.
- **FR-057**: Canonical Elsa 4 packages MUST remove the legacy memory interfaces and MUST NOT retain
  obsolete forwarding shims.
- **FR-058**: Any temporary legacy execution adapter MUST be isolated in an explicitly named
  compatibility module and MUST NOT become a dependency of canonical executable or runtime contracts.
- **FR-059**: Canonical type metadata MUST use the alias registry and schema descriptors and MUST NOT
  persist assembly-qualified CLR type names as a fallback.
- **FR-060**: Migration planning MUST identify every test whose subject changes or disappears and MUST
  preserve its behavioral objective through an explicit replacement, retention, or architect-approved
  removal entry.
- **FR-061**: Typed resume triggers MUST preserve the existing publication/start-authority and provider
  recognition rules.

#### DI activation-scope evidence gate

- **FR-062**: Before a DI activation lifetime is ratified, the project MUST compare burst-only,
  per-attempt child-scope, and safe conditional strategies under the same reproducible workloads.
- **FR-063**: The comparison MUST measure throughput, latency, allocations, disposal, dependency
  isolation, retry, and resumption behavior for constructor-free, transient-dependency,
  scoped-disposable, and long intrinsic-heavy workflows.
- **FR-064**: Engine-intrinsic control and value operations MUST create no CLR activity activation or
  activity DI scope.
- **FR-065**: A conditional activation fast path MUST be rejected unless it preserves the selected
  observable lifetime semantics for transitive dependencies and all supported service access paths.

### Key Entities

- **Workflow definition**: A code-first or dynamic authoring source that compiles once into an
  immutable executable artifact; it is not a workflow instance.
- **Workflow request/result contract**: The immutable typed documents entering and leaving a workflow
  instance.
- **Activity contract**: Versioned input, result, outcome, activation, policy, and stable-key metadata
  pinned by a published executable.
- **Activity node**: Immutable executable placement of an activity contract and its bindings.
- **Activity invocation**: One logical runtime execution of an activity node, stable across retries and
  resumptions.
- **Activity attempt**: One transient CLR activation and execution attempt for an invocation.
- **Input binding**: An immutable instruction selecting one legal source for an activity input.
- **Input snapshot**: The complete immutable materialized values pinned to one invocation.
- **Activity completion**: Atomic successful result plus authored outcome for one invocation.
- **Variable declaration/frame**: Authored lexical mutable-state declaration and one concrete runtime
  activation of its storage.
- **Activity state**: One immutable private checkpoint document for a stateful invocation.
- **Trigger delivery**: One identified typed payload that resumes a suspended invocation.
- **Structured result**: One typed value explicitly leaving a structured control-flow scope.
- **Activity fault**: Persistable normalized failure information made available only to a structured
  handler.

## Scope Boundaries

### In Scope

- Canonical value-role and binding semantics.
- Code-first workflow and activity authoring contracts.
- Source-generation and manual-fallback behavioral requirements.
- Durable invocation input, completion, variable, activity-state, and trigger semantics.
- Expression portability and purity requirements.
- Structured-scope, parallel, loop, and cycle data-flow validation.
- Elsa 3 import translation and canonical memory-interface removal.
- Activation-scope prototype and benchmark requirements.
- Reconciliation requirements for existing runtime binding, output capture, JavaScript write-back,
  and variable-scope behavior.

### Out of Scope

- Implementing the runtime redesign in this specification phase.
- Selecting the DI activation-scope strategy before benchmark review.
- Defining a general workflow fragment runtime abstraction.
- Preserving binary or source compatibility for canonical memory-block interfaces.
- Dual-running Elsa 3 and Elsa 4 workflow engines.
- Designer layout or presentation design beyond canonical authoring parity.
- General JavaScript/Liquid sandbox hardening unrelated to value access and mutation.
- Exactly-once external side effects; stable invocation identity supports idempotency but cannot make
  arbitrary external systems transactional.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Representative generated, foundational fluent, and dynamic definitions produce
  semantically equivalent canonical artifacts for 100% of the shared conformance fixtures.
- **SC-002**: Canonical executable and runtime dependency tests find zero references to
  `IMemoryBlock`, `IMemoryBlockReference`, memory-register contracts, legacy importer DTOs, or generated
  code-first authoring carriers.
- **SC-003**: Retry, suspension, worker-restart, and post-completion recovery tests preserve the same
  logical invocation identity, pinned inputs, committed activity state, and result in 100% of covered
  scenarios while creating a fresh attempt identity and CLR activation where required.
- **SC-004**: Control/data-flow validation rejects 100% of fixtures containing an unavailable required
  result, implicit cyclic latest-output lookup, or ungoverned concurrent variable write.
- **SC-005**: Loop and parallel conformance tests produce byte-equivalent ordered results across at
  least three different completion schedules for every fixture.
- **SC-006**: Expression conformance tests reject all fixtures using captured delegates, undeclared
  ambient value access, or binding-time state mutation, while JavaScript and Liquid fixtures with
  explicit parameters round-trip and evaluate successfully.
- **SC-007**: Elsa 3 import fixtures, including output-only references and combined
  expression/reference values, either produce canonical artifacts containing zero memory interfaces
  or return a path-specific diagnostic; no fixture drops a reference or silently falls back to
  canonical memory semantics.
- **SC-008**: Activity contract compatibility tests accept stable-key CLR renames and reject all
  incompatible type, removal, fingerprint mismatch, and missing activation-capability fixtures before
  user activity code runs.
- **SC-009**: Nonpersistable-value tests reject durable execution before user code in 100% of input and
  state cases and before downstream scheduling in 100% of result cases; explicitly transient fixtures
  cannot suspend or migrate.
- **SC-010**: The activation-scope evidence report covers all three candidate strategies and every
  workload named by FR-063, reports throughput/latency/allocation deltas from the same baseline, and
  records isolation and disposal outcomes before a strategy is accepted.
- **SC-011**: Intrinsic-only workflow fixtures execute with zero CLR activity activations and zero
  activity DI scopes.
- **SC-012**: Existing Design-only, Runtime-only, and combined deployment architecture tests continue
  to pass with no new Runtime-to-Design dependency.
- **SC-013**: Generated, manually described, and reflection-discovered versions of every representative
  activity fixture produce equivalent input, result, outcome, default, requiredness, stable-key, and
  editor metadata.
- **SC-014**: Before implementation tasks are approved, 100% of tests that directly depend on a
  removed memory subject have a recorded replacement, retention, or architect-approved removal.

## Assumptions

- The draft constitution's Design/Runtime split, artifact-only runtime, and import-only Elsa 3
  compatibility rules remain the governing provisional gates.
- The existing alias registry and behavioral artifact fingerprint remain the type and artifact
  identity authorities.
- Dynamic `Any` values retain their existing JSON storage and materialization representation; this
  redesign does not reintroduce open CLR-object polymorphism.
- The existing durable-value storage seam may be reused beneath role-owned runtime records, but it is
  not itself the public value-flow model.
- The runtime continues to provide at-least-once execution; external activities use the stable logical
  invocation identity as an idempotency key where supported.
- Sensitive-value policy details may be refined during planning, but no value-flow path may silently
  reduce required protection.
- Source generation is the recommended code-first path, while canonical runtime correctness remains
  independent of source-generator availability.
- The exact public spelling of illustrative API members may be refined during interface prototyping as
  long as the accepted semantic boundaries and developer experience remain intact.
