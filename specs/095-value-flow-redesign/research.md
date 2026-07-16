# Research: Role-Owned Workflow Value Flow

## D1 — The existing durable substrate is retained; the memory adapter is removed

**Decision**: Keep `RuntimeInputBinding`, `ActivityExecutionState`, `DurableValueState`, lexical
scope factories, bookmark records, and checkpoint commits as migration seams. Replace the
`InputArgument` → memory register → `OutputArgument` adapter between them with role-owned input
snapshots, attempts, private state, and atomic completion records.

**Rationale**: The runtime already compiles bindings and persists values. `RuntimeActivityInputMaterializer`
manufactures a private literal memory reference only to satisfy the activity API, while its expression
context implements a null memory register and no-op mutation. `ActivityOutputPublisher` performs the
inverse conversion after execution. This is adapter removal, not a new general persistence system.

**Alternatives considered**: Keep memory blocks (rejected: dual truth and mutable role conflation);
rename them as value slots (rejected: preserves addresses and `Get`/`Set`); create a universal
`ValueSource<T>` (rejected by ADR 0045).

## D2 — Inputs are pinned at the ActivityStarted checkpoint

**Decision**: Resolve the complete input binding set while changing an activity execution from
Scheduled to Running. Commit `ActivityInputSnapshot` and the first `ActivityAttempt` together with the
post-commit invocation intent. Invoke, retry, resume, and parent-completion callbacks hydrate only from
the stored snapshot.

**Rationale**: This is the last checkpoint before constructing user code and already owns the
idempotent handoff to invocation. Pinning inside the invoke handler leaves a crash window; reevaluating
on resume is the current defect.

**Alternatives considered**: Pin at schedule time (rejected: causal inputs may not yet be committed);
pin after activation (rejected: violates before-user-code durability); store only binding references
(rejected: retries would observe changed sources).

## D3 — Activity activation returns a transition instead of mutating an output context

**Decision**: `IActivity.ExecuteAsync` returns an `ActivityTransition`. Generic base classes preserve a
typed `TResult` for activity authors and erase it only at the engine boundary. Completion contains one
typed result plus one authored outcome. Suspension contains one typed private state document and typed
trigger registrations. Fault and cancellation remain distinct engine transitions. Explicit control
intrinsics may return named engine effects, but ordinary activity transitions cannot carry hidden
variable writes.

**Rationale**: Current `context.Set`, `SetOutcomes`, bookmark mutation, and context intent flags make
partial results and hidden effects possible. Returning a closed immutable transition makes the
checkpoint input complete and testable.

**Alternatives considered**: Keep mutable context with plain inputs (rejected: only hides memory
behind properties); return a dictionary of outputs (rejected: not atomic or typed); throw special
completion exceptions (rejected: control flow disguised as failure).

## D4 — An activation lease isolates construction, hydration, lifetime, and disposal

**Decision**: Replace `IActivityFactory.Create` plus `ActivityArgumentBinder` with
`IActivityActivator.ActivateAsync(ActivityActivationRequest)`. The returned async-disposable
`ActivityActivation` owns the fresh CLR object and any DI scope. The request carries descriptor data,
the pinned input snapshot, contract identity, attempt identity, private state, and optional trigger.
One hydrator assigns plain `[Input]` properties exactly once after constructor injection.

**Rationale**: This is the stable seam behind which burst, per-attempt, and conditional DI strategies
can be benchmarked without changing activity authoring or invocation semantics.

**Alternatives considered**: Put workflow data in DI (rejected: live invocation context/service
locator); construct and then expose public setters (rejected: inputs could change); bake per-attempt
scope into the activity API before benchmarking (rejected: unresolved evidence gate).

## D5 — Activity contracts are pinned and alias-typed

**Decision**: Each executable node pins an `ActivityContract` containing stable type identity,
contract version, schema fingerprint, input members, result schema/projections, and outcome keys.
Type descriptors use the alias registry and collection schema only. Publication proves the contract
and required activation capability exist before accepting the executable.

**Rationale**: Current input metadata stores assembly-bearing `typeName` strings and discovers some
defaults during construction. That conflicts with alias-only serialization and executable-always-runs.

**Alternatives considered**: Resolve CLR names at runtime (rejected: artifact depends on deployment
type names); fingerprint only the activity type (rejected: member drift is invisible); construct an
activity during execution to discover defaults (rejected: defaults are authored contract behavior).

## D6 — Result projections resolve through structural frame and causal lineage

**Decision**: Executable bindings name a producer node and stable result projection key, never a
concrete activity-execution id or a globally latest result. Snapshot materialization selects the unique
completed producer invocation visible in the consumer's structural frame and scheduling lineage.
Unavailable or ambiguous results fail before activation.

**Rationale**: Concrete execution ids do not exist at publication time. Latest-by-node is wrong for
loops, parallel branches, repeated nodes, and cycles. `ActivityExecutionState` already carries parent,
branch, iteration, and scheduling provenance needed for causal resolution.

**Alternatives considered**: Keep active output register as semantic truth (rejected: burst-local and
execution-id coupled); use newest completion timestamp (rejected: completion timing is nondeterministic);
allow cyclic latest output (rejected: cycles require explicit state transfer).

## D7 — Expression bindings are serialized pure functions over explicit parameters

**Decision**: Extend `RuntimeExpressionBinding` with deterministic named parameter bindings and
serialized evaluator options. Replace the canonical ambient `IExpressionExecutionContext` with an
immutable evaluation request containing parameter values, result type, capability profile, and
cancellation. Evaluators receive infrastructure through constructor injection. JavaScript exposes a
read-only `args` object; Liquid exposes only declared parameters. Stateful scripts remain activities.

**Rationale**: Explicit parameters are the only portable way to preserve JavaScript/Liquid while
removing ambient variables, outputs, workflow identity, service location, mutation, configuration,
time, GUID, and random backdoors.

**Alternatives considered**: Keep ambient reads but make mutation read-only (rejected: dependency and
determinism remain hidden); serialize delegates (rejected: not portable/persistable); allow all script
preprocessors under a `Pure` flag (rejected: capability injection can violate the flag).

## D8 — Variables use one Runtime-owned frame model and explicit Set intrinsics

**Decision**: `Variable<T>` becomes an authoring declaration handle only. Runtime owns
`VariableFrameState`, keyed by declaring scope, activation id, parent frame, and stable variable keys.
Root, container, and loop-iteration scopes share this model. A canonical intrinsic Set operation is
the only normal write path and commits before downstream scheduling. Potential concurrent writes fail
publication unless an explicit deterministic merge/reduction applies.

**Rationale**: Current values use three encodings: workflow `DurableValueState` rows, container JSON in
activity metadata, and iteration scheduling metadata. Mutable scope diff/write-back also lets scripts
and arbitrary activities hide writes.

**Alternatives considered**: Keep `VariableScope` dictionaries and snapshot them (rejected: runtime
truth remains mutable process memory); identify variables by name (rejected: names are not durable
identity); allow activity context writes (rejected: hidden effects and retry ambiguity).

## D9 — Code-first authoring compiles through existing authored state

**Decision**: Add `WorkflowDefinition<TRequest,TResult>` and builders in Workflows Design Core. A
sequence builder appends generated activity method calls in source order; structured builders own
their lexical regions. The foundational API uses explicit `.From(source)` and `.Value(literal)`.
Generated activity methods accept authoring-only `ActivityArgument<T>` conversions and return handles
with `Node`, `Result`, `Outputs`, and `Outcomes`. A Roslyn helper in the consumer workflow project
enumerates referenced activity contracts and generates the facade. Both paths produce
`WorkflowDefinitionState`, then use the existing publisher/compiler.

**Rationale**: This yields ordinary method-call syntax without making activity CLR instances into
blueprint objects and without adding a second executor. Generating in the consumer prevents activity
packages from depending on Design or Roslyn.

**Alternatives considered**: WWF-style instantiated object graph (rejected: object identity becomes
definition state); static `Build` convention (rejected developer discoverability); generated
configurator chains (rejected preferred DX); reflection-only facade (rejected compile-time typing).

## D10 — Elsa 3 memory references are resolved as an importer-local graph

**Decision**: First pass records every Elsa 3 memory-reference occurrence with JSON path, activity,
direction, stable property key, and structural frame. Second pass lowers unique producer/consumer and
variable relationships into canonical bindings. Output-only and combined expression/reference values
are preserved. Dangling, multiple-producer, ambiguous, cross-subtree, computed accessor, mutation, and
custom shapes produce path-specific diagnostics. Static JavaScript/Liquid ambient reads may be
rewritten into declared parameters; unsafe dynamic access fails import.

**Rationale**: `Elsa3ActivityToState.TryGetArgument` currently requires an expression and ignores
`memoryReference`, silently dropping the exact output wiring import must preserve.

**Alternatives considered**: Instantiate memory blocks during import (rejected: contaminates canonical
model); guess producer by traversal order (rejected: unsafe/ambiguous); keep scripts unchanged
(rejected: ambient access would reintroduce memory semantics at runtime).

## D11 — Compatibility is a major-version migration, not a forwarding layer

**Decision**: Delete `IMemoryBlock*`, `IMemoryRegister`, `Argument`, `InputArgument`, `OutputArgument`,
memory-backed `Variable`, runtime memory seeding/publishing, delegate expression converters, ambient
runtime expression carriers, and synthetic workflow-value channels after consumers migrate. Preserve
behavioral test objectives through the ledger. In-flight Elsa 4 executions using the former state
shape are rejected by an explicit artifact/state compatibility gate unless a versioned upcaster can
fully reconstruct the new role records; there is no live dual-read mode.

**Rationale**: A shim would let the obsolete abstraction remain a public design option and would make
the migration bidirectional. The user explicitly approved the clean break.

**Alternatives considered**: Obsolete forwarding interfaces (rejected); compatibility mode in
canonical Runtime (rejected by §E2.7); automatic best-effort live-state migration (rejected: missing
pinned snapshots cannot be reconstructed deterministically).

## D12 — DI lifetime is selected only after the real benchmark and semantic gates

**Decision**: Implement burst-only, per-attempt child-scope, and a conditional candidate behind the
activation lease. Measure the workloads in `contracts/activation-scope-benchmark.md`. A candidate is
ineligible if it violates isolation, transitive dependency, retry/resume, or disposal semantics. The
winning lifetime is recorded by amending ADR 0045 or a focused successor before it becomes a public
contract.

**Rationale**: Current invocation uses the ambient service provider and shares scoped services across
a burst. Per-attempt scope is cleaner but may affect micro-activity throughput. Intrinsic operations
must first remove avoidable activation overhead from the comparison.

**Alternatives considered**: Select per-attempt from architecture alone (rejected: performance gate);
keep burst scope from Elsa 3 precedent (rejected: isolation is observable); conditional on constructor
parameter count only (rejected: transitive dependencies and service access invalidate the proof).
