# Workflow Value Flow Uses Role-Owned Bindings And Immutable Invocation Records

Status: accepted (2026-07-16; ratified through an architect grilling session). §5 amended (accepted 2026-11-12; decisions D-1, D-5, D-6 and
D-15): expressions no longer need to be deterministic. §2 amended (accepted 2026-11-12; decisions D-10 and
D-11): null, omitted values and `undefined`. §6 amended (accepted 2026-11-12;
decisions D-4 and D-14): activities select their outcomes explicitly, several per completion.

Elsa's canonical workflow and activity model will not use `IMemoryBlock`,
`IMemoryBlockReference`, a memory register, or a universal runtime value-reference interface.
Workflow requests, activity inputs, activity results, variables, private activity state, and resume
triggers remain distinct value roles. Immutable executable bindings connect those roles, while
role-owned runtime records store their materialized values.

CLR activities use a fresh child DI scope for every execution attempt. The scope and its transitive
dependencies are disposed when that attempt ends. Retries and resumptions create a new activity and
scope; engine intrinsics create neither. This lifetime was selected from the semantic and benchmark
evidence recorded below.

## Context

Before this decision was implemented, Elsa Foundation contained two competing value models.

The runtime artifact already carries compiled `RuntimeInputBinding` declarations, projects workflow
inputs and variables from `DurableValueState`, and records execution state in runtime-owned stores.
Alongside that model, the activity and expression layers still carry Elsa 3's memory abstraction:

- `Argument` owns a `Func<IMemoryBlockReference>`.
- `InputArgument<T>` and `OutputArgument<T>` wrap memory references rather than materialized values.
- `IExpressionExecutionContext` exposes generic memory-block `Get`, `Set`, and declaration APIs.
- Runtime input materialization manufactures memory references and seeds a per-activation memory
  register before calling activity code.
- Activity outputs are repackaged as memory-backed output arguments before publication.

That second path is not the canonical Elsa 4 persistence model. It is an Elsa 3-shaped adapter
between otherwise newer compile-time and runtime contracts. Keeping it would preserve two sources of
truth, blur the distinctions between immutable inputs, immutable results, and mutable variables, and
encourage a new universal `ValueSource<T>` or memory-register abstraction to spread through the new
runtime.

The design also needs a first-class code-first authoring experience. Elsa must support familiar CLR
activity authoring and composition without treating activity objects as workflow-instance state or
serializing a WWF-style CLR object graph.

This decision belongs to the [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).
It is constrained by the draft constitution's Design/Runtime split, artifact-only runtime, and
import-only Elsa 3 compatibility rules. The constitution remains draft; this ADR records an accepted
architecture decision within those provisional gates rather than ratifying a new constitutional
gate.

The canonical Elsa meanings introduced or refined by this decision are recorded in the
[Elsa glossary](../glossary/elsa.md). This ADR describes their relationships and does not introduce a
competing terminology source.

## Decision

### 1. Keep value roles distinct

Elsa uses the following value roles:

| Role | Mutability and lifetime | Runtime owner |
| --- | --- | --- |
| Workflow request | Immutable and pinned for one workflow instance | Workflow invocation record |
| Activity input | Immutable and pinned for one logical activity invocation | Activity invocation input snapshot |
| Activity result | Immutable and committed atomically on successful completion | Activity completion record |
| Variable | Explicitly mutable within a lexical runtime scope frame | Variable-scope state |
| Activity state | Immutable checkpoint document replaced on a stateful activity transition | Activity invocation state |
| Resume trigger | Immutable payload for one resumption attempt | Trigger/bookmark delivery record |

Workflow results are immutable typed completion documents. Faults, cancellation, and suspension are
execution transitions, not successful results. Persistable fault information is normalized into a
scope-local fault record for structured handlers; arbitrary CLR exceptions are not normal workflow
values.

There is no public runtime interface that makes these roles interchangeable.

### 2. Compile role-specific input bindings

Each authored activity input compiles to exactly one immutable binding instruction. The canonical
binding alternatives are:

- literal value;
- workflow-request member;
- variable read;
- causally available activity-result projection; or
- expression definition with explicit parameters.

The common serialized shape exists only because one input slot must contain one legal alternative.
It has no address, `Get`, `Set`, or memory-block identity. A producer node in an activity-result
binding is resolved relative to the consumer's structural frame and causal control-flow lineage; it
never means the globally latest execution of that node.

The canonical executable model in this ADR is the execution material carried by the constitutional
`WorkflowExecutable`; this decision does not introduce a separate persisted "blueprint" artifact.

Publication normalizes every input to a concrete binding. Required omissions fail validation;
pinned defaults and optional `null` values become explicit literal bindings. Missing dynamic values
never silently become `default(T)`.

**Amendment: null, omitted values and `undefined`.**
These rules apply to native and imported workflows alike. Where a workflow needs Elsa 3 behaviour, it says so through
a visible policy, never through where it came from.

- **Scope.** These rules apply to declared members: activity inputs and workflow-request members. Inside a JSON value
  (`Any`), a missing property and a `null` property stay distinct, because the data is stored as received (ADR 0091,
  decisions 3 and 8).
- **Requiredness and nullability are separate properties** of an activity input and of a workflow-request member. A
  member can be required and nullable, or optional and non-nullable.
- **Explicit `null` is a value, not an absence.** It is accepted for every nullable target: activity inputs,
  variables, workflow outputs and the values of the `Set` / `SetOutput` intrinsics. The intrinsics' current refusal of
  an explicit null for a nullable target is a defect to fix.
- **`null` for a non-nullable target** fails with `VF-ACT-004`, unless an explicit *default-on-null* policy applies.
  That policy is **workflow-level** and substitutes the target's **type default**, independently of any declared
  default, instead of failing. A nullable target always preserves explicit null; a non-nullable target without a
  usable type default rejects it (below). The policy is visible in the workflow and pinned in the executable.
  - **A type without a non-null default** (for example a non-nullable `String`, or a JSON object) leaves the policy
    nothing to substitute, so `null` for such a target still fails with `VF-ACT-004`. Where an activity handles
    `null` correctly, its input is declared nullable instead; the policy never works around an activity's contract.
- **An omitted member.** Requiredness is checked first: an omitted *required* member fails even when a declared
  default exists — a declared default never satisfies a required member. Declared defaults apply only to omitted
  *optional* members, which take their declared default first.
  - An optional member without a declared default materializes as `null` when its type is nullable, and otherwise as
    its type's default. This is defined behaviour from the contract, not a silent fallback. A non-nullable type
    without a non-null default needs a declared default; publication refuses the member without one (`VF-ACT-003`).
  - An omitted required activity input still fails publication (`VF-ACT-003`).
  - An omitted required workflow-request member is refused when the workflow instance is admitted, before the start
    checkpoint, on every start path, not when a later step reads it. A rejected request executes no activities and
    schedules no workflow work.
- **An undeclared name is not an omitted optional member.** Reading a value no contract declares remains an error.
  The sentence above about missing dynamic values keeps applying to it. For names a script reads:
  - a name written literally in the script that resolves to nothing is a publication error that prevents publication;
  - a computed name (`variables[name]`, `getVariable(expr)`) is looked up at run time
    ([ADR 0089](0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md), R3). A computed
    name that resolves to nothing reads `undefined` with a warning, and the read is marked absent. Not `null`, which
    would erase the difference between null and missing; if the `undefined` leaves the script, the boundary rule below
    applies visibly. In Liquid a missing value is nil, and the recorded warning carries the difference.
- **`undefined`.** By default the JavaScript-to-workflow boundary keeps its standard materialization: a
  binding whose result is `undefined` is an error ([ADR 0083](0083-javascript-binding-grammar-is-pinned-at-publish.md)),
  and an object property whose value is `undefined` is dropped, with a diagnostic, because a lossy conversion is
  declared, never silent. An `undefined` **array element or sparse hole** instead becomes a `null`
  placeholder, with a path diagnostic, so positions are preserved — dropping it would shift every later index; this
  holds under either policy, because an array's positions can never shift. An element that was already explicit `null`
  needs no conversion diagnostic, and a string an explicit `JSON.stringify` produced is unchanged. `NaN` and
  `Infinity` become `null`, also with a diagnostic. The JavaScript adapter
  contract in the [ADR 0046](0046-output-binding-coercion-uses-pinned-value-representations.md) amendment holds these
  rules. A workflow may select the *undefined-as-null* policy:
  - an `undefined` result materializes as `null`;
  - an `undefined`-valued object property is kept as `null`, at every depth;
  - the policy acts only at the boundary. It does not change JavaScript's own semantics, and it does not rewrite a
    string an explicit `JSON.stringify` produced;
  - an input binding that produces no value and a script activity that completes without returning a value remain
    distinct cases. An expression binding and a Script completion value have no `return` statement — their value is
    the expression's result — so the no-return check applies only to function-body script activities whose result is
    consumed, and the undefined policy applies to the value each form actually produces. Each case is defined by the
    implementing spec.
- **Both policies are off by default for native workflows,** and are **recorded on the workflow and pinned in the
  executable,** so they take part in its artifact hash
  ([ADR 0038](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)).
  - A host or importer enables them; the workflow carries the result. A native workflow that selects neither keeps the
    default boundary behaviour above.
  - The Elsa 3 converter feature has one option that selects the policies Elsa 3 parity needs, and it records them on
    every workflow it converts.
  - A converted workflow that is later edited, exported or run on a host without the converter keeps behaving the
    same.
  - This replaces the alternative of a converted-definition marker, under which converted workflows would run under
    different rules than native ones.

### 3. Materialize and pin inputs before activation

For durable execution, the runtime evaluates an invocation's bindings, validates type and
persistability, and commits the invocation identity plus immutable input snapshot before constructing
or invoking user activity code. Retries and resumptions retain one stable logical `InvocationId` and
the same input snapshot while receiving a new `AttemptId` and a fresh CLR activity object.

Successful completion commits one typed result payload and one authored outcome selection (see the
§6 amendment for decision D-4) before any downstream work is scheduled. Named outputs are typed projections from that atomic result; they are
not independently writable output slots. A committed invocation is not re-executed merely because
downstream scheduling was interrupted.

Nonpersistable input, state, or result values are legal only under an explicit transient execution
policy. Such execution cannot suspend, migrate, or depend on durable retry. Reconstructible
burst-local caches from [ADR 0031](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)
remain optimizations and are not workflow value sources.

**Amendment (issue #977): opt-in per-pass input re-materialization for structural evaluations.** A
structural composite may implement the marker `IRuntimeRematerializeInputsOnChildCompletion` to have
its inputs re-materialized from live committed variable frames before each child-completion/child-fault
evaluation, so a loop condition observes state its body committed (an [ADR 0027](0027-scoped-variable-references-include-declaring-scope.md)
scope mutation written back per pass; iteration identity per [ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md)).
The pinned invocation record is unchanged: the committed `InputSnapshot` stays immutable and the
re-materialized snapshot is transient — recomputed deterministically from committed frame state on any
redelivery — so retries and history retain the original record. `While` is the sole implementer; all
other composites keep the immutable-snapshot behavior above.

### 4. Use explicit lexical variables and structured results

A `Variable<T>` is an authored declaration handle, not runtime storage. Variable declarations are
lexically scoped to workflow and structured builder regions. Each activation of a region creates a
runtime scope frame; loop-body declarations therefore receive one frame per iteration. Descendants
may explicitly read or `Set` a visible ancestor variable, while siblings cannot address each other's
locals.

`Set` is a graph-visible intrinsic operation. Activity completion and expression evaluation do not
silently write variables. A nested scope's typed `Return` is the normal way it produces an immutable
value for its parent; an explicit `Set` of a visible ancestor variable remains a separately visible
side effect.

**Amendment (issue #1004): explicit output-capture edges.** A pinned activity output capture is a
narrow second graph-visible variable-write form: it is an authored, inspectable engine-owned binding
edge from a named projection of the activity's atomic result to a variable declaration, not an
activity-code, expression, or ambient completion side effect. Workflow and container targets resolve
and commit under [ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md).
The capture's frame mutation participates in the producing completion checkpoint; unbound activity
completion and activity transitions still cannot write variables.

**Proposed amendment.**
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) (proposed)
would add a third, declared variable-write form (changes an activity step makes, committed with the step) and a
step-read channel at invoke beside input materialization. Until it is accepted, this section stands as written.

Potentially concurrent ordinary writes to the same variable fail validation unless an explicit,
deterministic merge or reduction governs them. This refines ADR 0027's previously open concurrent
write policy. Reads occur when the consumer invocation materializes its inputs, and a completed `Set`
is committed before sequential downstream work observes it.

Activity outputs are lexical and causal. Structured scopes export data through one explicit typed
result:

- every successful branch of `If<T>` returns `T`;
- `ForEach<TItem,TResult>` collects by stable iteration identity and source order;
- parallel results use stable branch identities and an explicit merge; and
- cyclic back-edges carry data through explicit state transfer rather than a latest-output lookup.

Required output bindings must be definitely available on every path to their consumer. Conditional
data requires an explicit optional value, union, or branch merge.

### 5. Make expressions serialized, explicit, and pure

JavaScript, Liquid, and other expression languages remain first-class. Their canonical definition
contains a language identifier, source string, expected result type, explicit named parameter
bindings, and evaluator options. Input-binding expressions cannot retain CLR delegates or captured
closures, or change engine-owned workflow state: variables, workflow outputs, correlation ID, instance name or
outcomes. They read workflow values only through the read surface defined in
[ADR 0089](0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md), and evaluate against
a read-only Elsa context. (This section stays neutral on how that read surface works; ADR 0089 and its amendments
define it.)

Stateful scripts are activities that receive inputs and return typed results. Their results can feed
an explicit `Set` operation. This decision supersedes the canonical-model portion of ADR 0030 that
kept JavaScript variable write-back and ambient output accessors: those surfaces may remain only as
isolated migration compatibility while the new model is introduced. (Proposed amendment:
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
would let RunJavaScript change variables as a declared write form; input-binding expressions stay pure.)

Expressions do not have to be deterministic. They may read the current time, generate random values and create
GUIDs. See the amendment below.

**Amendment (decisions D-1, D-5 and D-6): the execution contract replaces expression determinism.** This amendment replaces the original rule of this section, which admitted time, randomness and other
nondeterministic data only as pinned parameters or through a separately specified deterministic capability. That
rule is withdrawn. The withdrawn design for such a capability (the ADR 0088 draft) is not needed.

*Why.* Elsa 4 resumes from committed state, not by replaying history, so an expression does not have to produce the
same value twice. What a user relies on is the execution contract: a recorded step never runs again, recovery
runs forward from the last recorded point, and a re-run is a new execution with new values, whether it starts at the
beginning or from a point midway (an activity rescheduled or scheduled through a runtime alteration,
[ADR 0049](0049-runtime-alterations-use-snapshotted-atomic-jobs.md)). History is never rewritten. A midway re-run is
a re-execution: a new invocation that reads fresh inputs, while retrying the same invocation keeps its inputs
(ADR 0092 decision 8). Code that copies the old input snapshot on a reschedule does not meet this rule. Idempotency of external effects belongs to
the workflow designer; the engine supplies stable invocation identities to build idempotency keys from. Around an
external effect it knows of (D-6 and D-5 say which) the engine commits right before and right after it, so the effect lies
between two recorded points.
This sentence states the guarantee only: how the engine provides it is the execution-contract details amendment of
§3, ADR 0032 and ADR 0020 (see *Follow-ups* below). §3 already provides the part that matters here: a committed input snapshot is reused on retry and resumption,
so a value an expression produced, once committed, does not change.

- **D-1: clock, randomness and GUIDs.** Input-binding expressions and scripts (RunJavaScript) may read the current
  time, generate random values and create GUIDs.
  - Evaluators run in UTC with the invariant culture. The current time comes from an injectable clock, so tests can
    control it; spec 095 names the .NET type.
  - UTC is the engine's clock, not a restriction on what a workflow stores. A time with its offset
    (`DateTimeOffset`) keeps that offset, and a workflow may store an IANA time zone ID (for example
    `Europe/Amsterdam`) and convert explicitly with a built-in date helper. The ADR 0046 amendment, *Dates*, owns
    every date and time-zone rule; the implementing spec defines the helpers.
  - The engine provides built-in helpers for dates and GUIDs. Elsa 3 functions for the same purpose, such as
    `newGuid()` and `new Date()`, convert directly, and their names alone never cause a refusal.
  - An evaluation that was never durably recorded may produce different values when it runs again. A deliberately
    repeated evaluation, such as a loop condition on each pass, produces fresh values.
- **D-5: functions implemented in .NET.** A host may install functions into an expression engine whose
  implementation is .NET code, as `getVariable` already is. By default only JSON values cross the boundary between
  the script and such a function. Live .NET objects do not cross it by default. A host may opt in to .NET interop
  through one replaceable engine strategy that a feature provides. The ADR 0046 amendment (V10) states the rule.
  - **Input-binding expressions.** By default input-binding expressions get no live objects. When a host enables .NET
    interop, it applies to input-binding expressions as it does to script activities: a JavaScript input expression
    takes the same input as a script in a JavaScript script activity.
    - Whether such an expression is treated as possibly effectful, with a commit before and after its evaluation, is
      decided by the opt-in's implementing spec.
    - It may use .NET objects while it evaluates, but what it returns is a storable value under ADR 0046, or the
      evaluation is refused. No live object enters the input snapshot.
    - Direct writes to Elsa-owned workflow state stay blocked, as for every input-binding expression.
    - The publish warning and the Studio marker that ADR 0083 defines for general .NET access in script activities
      apply to such a binding in the same way.
  - **Names.** Installed functions follow one naming rule, modelled on the type alias registry (ADR 0035):
    - reserved: the read roots (`variables`, `input`, `activities`, `workflow`, `args`) and Elsa's built-in functions;
    - module and host functions use a namespace prefix, and a duplicate registration fails at startup instead of
      silently overriding another;
    - a script that declares a reserved name (`const variables = …`) gets a publish diagnostic.
- **D-6: idempotency belongs to the designer.** An effect outside Elsa's own state, such as an HTTP call or a .NET
  service call, cannot be rolled back by Elsa. The engine supplies stable invocation identities; the workflow designer
  owns idempotency (see *Why*). A call is never refused or skipped because of what it does.
  - **Undeclared functions are effect-free.** A registered function without a declaration is treated as effect-free:
    no commits are placed around an input evaluation because it calls one. Elsa's built-in functions, such as
    `newGuid()`, carry no declaration.
  - **The effectful declaration.** A host may declare a function effectful when it registers it. Only then does the
    engine place a commit before and after an input evaluation that calls it. This is one optional flag for the host
    developer; workflow engineers never see it. It is the only effect metadata a function carries: there are no
    effect profiles.
  - The declaration never refuses, skips or deduplicates a call. It narrows the window in which an effect can repeat
    and does not close it: a call can succeed before its result is committed and then run again. Idempotency stays
    with the designer either way.
  - *Accepted cost.* An effectful function registered without the declaration gets no boundary: after a crash its
    effect can repeat anywhere in the step. External business operations belong in activities, not in functions
    called from input bindings.

*Unchanged by this amendment:*
- the canonical serialized definition;
- the read-only Elsa context during input evaluation;
- the first paragraph of this section;
- the rule for stateful scripts below. Whether scripts and activities may change engine-owned state is
  decided outside this amendment, in [ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
  and the decisions that follow from it.

*Follow-ups owned elsewhere:*
- **ADR 0083 and spec 095's expression contract:** a new capability-profile name or version. `binding-pure-v1` still
  forbids time and randomness and is replaced there. The function effect profiles of spec 095 (R115 to R119) go with
  it, in the same amendment; the single effectful declaration of D-6 replaces them.
- **The execution-contract details of §3, ADR 0032 and ADR 0020.** They define how the engine keeps the guarantee
  stated in *Why*, that it commits right before and right after an external effect:
  - a stable identity for an input evaluation that may have effects;
  - the durability boundary around it;
  - a review of the per-pass input re-materialization amendment of §3, whose re-materialized snapshot is said to recompute "deterministically" on
    redelivery, which no longer holds for an expression that reads the clock;
  - an audit of evaluation sites that evaluate twice and expect the same result.
- **The replay justification of the `Inline` activity,** which relies on `binding-pure-v1`, is revisited in the
  implementing spec.

**Amendment (issue #2717): workflow-instance metadata as a declared parameter.** An expression parameter
binding may also read metadata of the running workflow instance: its correlation ID or its instance ID
(`{"kind":"WorkflowInstance","member":"correlationId"|"instanceId"}`). This is a fifth canonical parameter
kind beside literal, workflow request, variable and activity result; it is not an ambient accessor, because an
expression sees the value only through the parameter it declares, and the capability profile (now `binding-readonly-v1`, ADR 0083) is unchanged by it.
The value comes from committed runtime state, never live memory: the instance ID is the workflow execution ID,
and the correlation ID is the latest committed identity durable value, which the workflow-started checkpoint
seeds from the start request and a `SetCorrelationId` intrinsic replaces. An instance without a correlation ID
binds JSON null. Like every input read, a change is seen by inputs materialized after it commits; snapshots
already pinned are not re-evaluated, and a re-materialized snapshot (replay, a `While` pass) reads the same
committed value. A runtime build that predates this kind cannot read an executable that uses it; before GA this
is accepted, not mitigated. The Elsa 3 converter lowers `getCorrelationId()` and `getWorkflowInstanceId()` to it
(spec 247).

### 6. Separate activity contracts, activations, and invocations

An activity contract is immutable versioned metadata. A published executable pins the activity type
identifier, contract version, stable input/result/outcome keys, and schema fingerprint. A CLR member
rename can retain its stable key; incompatible schema changes require a new contract version.

A CLR activity object is a transient activation for one execution attempt. Constructor parameters
are services only. Workflow data is hydrated into plain `[Input]` properties before execution;
`required init` is the preferred authoring form and conventional setters remain a compatibility
fallback. The generated activation path owns constructor injection and one-time hydration. Runtime
contexts do not provide a general service-locator escape hatch that could make activation-scope
semantics invisible.

Ordinary activities return one immutable typed result. Stateful or suspending activities additionally
declare one immutable `TState` checkpoint document. Initial execution and resumed execution are
distinct callbacks; resumption supplies committed state plus one typed trigger payload. Activity
fields and injected services never survive a suspension.

The activity transition algebra distinguishes complete, suspend, fault, and cancel. An activity
transition does not contain hidden variable mutations; explicit `Set` nodes remain the only normal
variable-write mechanism. (Proposed amendment:
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) would add a
third write form, with `context.Variables.Set` as an exception to the sentence below; see §4.) The activity
invocation contract returns that transition directly; it does
not publish successful values or outcomes through mutable execution-context `Set` methods.

**Amendment (decision D-4): activities select their outcomes explicitly, several per completion.** A successful completion carries an *outcome selection* instead of exactly one outcome.

- **A selection** is one of three things:
  - unspecified, which means `Done` (see *The `Done` rule*);
  - one or more declared outcome names;
  - explicitly empty, meaning a successful completion with no outgoing path.

  Unspecified and explicitly empty stay distinct through completion, parent and join handling, persistence and
  recovery. An explicitly empty selection ends this invocation's outgoing paths; it does not itself cancel other
  running branches. An empty selection sends a dead arrival on every outbound connection
  ([ADR 0064](0064-flowchart-infers-joins-from-propagated-dead-paths.md)); a downstream join counts it as an arrival
  and never waits on it. It is a successful completion, so the step's pending engine-owned changes still commit. A parent
  that does not route by outcomes (a sequence, the workflow root, a loop body) treats it like any other successful
  completion.
- **The `Done` rule.** `Done` means "completed without choosing a branch". An activity whose completion may choose no
  branch declares `Done` (a WriteLine, a Sequence, a RunJavaScript); an activity whose every completion chooses a branch
  does not (an If with `True` and `False`, a Switch, a SendHttpRequest with expected status codes). `Done` is never
  selected together with a branch. An unspecified selection on a node without `Done` is a defect in the activity's
  code: it faults with `VF-ACT-006`, and the fault says that the activity completed without selecting an outcome and
  declares no `Done`. Whether publication can also catch such an activity before run time is settled in the
  implementing spec (S-3). This is the shape Elsa 4's activities already have. Elsa 3, where `Done` fired on every
  completion next to any branch, is converted by mapping its `Done` onto every branch (below).
- **Selecting.** A C# activity returns the selection in its `Complete` transition.
  - A script selects by **returning an explicitly recognized completion/routing form** that carries the result and the
    outcome selection as separate channels. The native script surface gains no `setOutcome` / `setOutcomes` setters: the
    sandbox stays closed and side-effect-free, and the routing decision stays a value the script returns rather than a
    host mutation. The form needs no ambient `activity` object; its exact spelling — a returned data representation or a
    pure constructor — and its recognition and validation are settled in the implementing spec (S-3).
  - **Routing and business data stay separate.** An ordinary returned value (a string, number, object, array or null)
    is the activity's result only and selects no outcome. A completion that selects outcomes does so only through the
    recognized form, which still carries the result on its own channel. This replaces RunJavaScript's routing by return
    value and its `Unmatched` outcome, so returning data can no longer change the route by accident: "nothing
    selected" is `Done` (not a silent `Unmatched`), an unknown name faults (not a silent `Unmatched`), and several
    outcomes are expressible.
  - **Ending routing by return value is deliberate.** A plain returned value is data and never selects a route; only the
    returned completion/routing form selects. A RunJavaScript workflow that relied on returning an outcome name changes
    behavior, so the change is never silent: publication warns when a RunJavaScript script returns a string literal that
    equals an outcome name connected on that node. The implementing spec (S-3) defines the diagnostic.
  - A selection takes effect only on successful completion. A fault or cancellation dispatches nothing, and a selection
    never dispatches a branch while the script is still running.
  - The returned selection is therefore part of the step's transaction (D-3): it is the completion's own data, carried
    back with the result, not a mutable `Set`, so it needs no exception to the sentence above about mutable `Set`
    methods.
- **Declared outcomes per node.** The set of outcomes a node may select is resolved at design time. It combines the
  activity's statically declared outcomes with outcomes derived by design-time providers from the node's
  configuration. For example, `SendHttpRequest`'s expected status codes and RunJavaScript's possible outcomes each
  become a port.
  - Studio and publication use the same provider rules.
  - When provider-relevant configuration changes, the ports refresh and affected connections are exposed for repair.
  - Publication pins the resolved per-node set in the executable, and refuses a connection whose source port is not in
    its source node's set.
  - Outcome names are never invented at run time.
- **Validation in two layers,** depending on how a name is written:
  - a literal name (a selection of `'Approved'`) that is not in the node's set is a publish diagnostic, so a typo is
    caught before run time;
  - a computed name (a selection of `variables.status`) is checked at run time against the node's pinned set. An unknown
    name faults the step with `VF-ACT-006`. An activity may deliberately route unfamiliar values to one of its **own**
    declared catch-all outcomes (SendHttpRequest's unmatched status, a `Review` or `Unmatched` the activity declares) —
    that is an explicit routing decision inside its contract, distinct from silently repairing an invalid name, and the
    catch-all must itself be in the node's pinned set and never excuses an undeclared result projection. A generic
    per-node fallback setting that turns any invalid name into another outcome is **deferred** until a concrete use case
    justifies it. Dynamic selections stay possible, within the set the designer declared.
- **Unconnected outcomes.** A declared outcome with no connected edge is valid; selecting it ends that path.
- **Several outcomes.** Whether a node's completions may select more than one outcome is resolved at design time with
  its outcome set, from the activity's static declaration or a provider over the node's configuration (a Switch may
  in "match any" mode only), and pinned per node.
  - The runtime rejects a selection of several outcomes on a node that does not permit it.
  - The connections of every selected outcome are scheduled. When two of them lead to the same node, each is an
    arrival at that node. **By default** its synchronizing implicit join
    ([ADR 0064](0064-flowchart-infers-joins-from-propagated-dead-paths.md)) accounts for the arrivals of one activation
    and runs the target once (two arrivals, one run). Loop re-entry and independent activations stay distinct
    executions. This amendment defines no other join behaviour; whether a target may run once per arrival belongs to
    the design of join behaviour as a whole, not to the outcome contract. S-3 carries the detail.
  - Path analysis, joins ([ADR 0064](0064-flowchart-infers-joins-from-propagated-dead-paths.md)), the unordered-write
    warning ([ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md), spec 266)
    and inspection treat a node that may select several outcomes as an inclusive fork.
  - This replaces the "one outcome per completion" invariant that was proposed before this amendment.

*Effect on Elsa 3 conversion.* These become convertible:
- Elsa 3's `setOutcome` / `setOutcomes`;
- Switch in "match any" mode;
- `Complete` with several outcomes.

Elsa 3 had no checked outcome set. A computed selection such as `setOutcome(variables.status)` remains supported,
but the converter must establish a suitable set of possible names and preserve the original routing behavior.
S-3 defines supported derivations and declarations, using the node's `PossibleOutcomes` and supported script analysis.
Literal names found in a script are evidence, not necessarily an exhaustive set for a computed selection. The
converter need not know which outcome a particular execution will select. If it cannot establish faithful conversion,
it refuses with actionable guidance, such as supplying a suitable outcome declaration (faithful-or-refuse).

Elsa 3's `Done`, which fired next to any branch, maps onto every branch outcome of the converted node, as the
converter already does for If, Switch and DispatchWorkflow; SendHttpRequest with expected status codes follows the same
rule. A RunJavaScript with no selection completes with `Done`, in Elsa 3 and here. The converter's rule that maps
Elsa 3's `Done` to `Unmatched` (spec 239 R8, spec 236 member map) existed only because of routing by return value; it
becomes `Done` to `Done`. S-3 must prove this preserves routing through conversion, publication and execution,
including connected RunJavaScript nodes whose `Done` was converted to `Unmatched`.

*Left to the implementing spec (S-3):*
- the spelling, recognition and validation of the script's returned completion/routing form (result and selection on
  separate channels, no ambient `activity` object);
- removing RunJavaScript's routing by return value and its `Unmatched` outcome, and the converter's R8 rule, with
  conversion, publication and execution evidence;
- the publish warning for a RunJavaScript script that returns a string literal equal to a connected outcome name;
- whether publication flags an activity without `Done` that can complete with no selection;
- the supported derivations and declarations for faithful conversion of computed outcome selections;
- the shape of the selection in the transition and in persisted completion records;
- how a multi-outcome declaration appears in the activity contract and in Studio;
- the activity-declared catch-all handling (a generic per-node fallback is deferred until a concrete use case);
- the changes to scheduling and joins — including when the default "two arrivals, one run" applies — and spec 266's
  exclusivity analysis.

### 7. Provide a generated method-call authoring facade

The foundational fluent builder uses explicit `.From(typedSource)` and `.Value(literal)` operations.
Generated code-first activity methods may provide the preferred method-call syntax by accepting an
authoring-only `ActivityArgument<T>` carrier with implicit conversions from literals, workflow inputs,
variables, activity outputs, and expressions.

`ActivityArgument<T>` is syntax, not architecture. It is confined to an authoring assembly and is
immediately lowered to a role-specific canonical binding. Canonical executable and runtime packages
must not reference it. The authoring API distinguishes omitted arguments, explicit `null`, and
`default(T)`; explicit factories provide an escape hatch for conversion ambiguity.

Generated activity-call handles expose a consistent shape:

- `Node` for executable-node identity;
- `Result` for the whole typed result;
- `Outputs` for named result projections; and
- `Outcomes` for typed control-flow outcomes.

Outcomes select control-flow edges and remain separate from result data. Fault, cancellation, and
suspension remain runtime transitions rather than authored successful outcomes.

Source generation is the recommended code-first path, emitting typed contract metadata, activation
and hydration support, and the method-call facade. Manual definition metadata and a reflection-based
compatibility path remain available; the canonical runtime model does not depend on Roslyn.
Generated, manually described, and reflection-discovered activity contracts must agree on plain
annotated-property inputs, stable keys, requiredness, pinned defaults, editor metadata, result schema,
and outcome schema.

### 8. Make workflow definitions compiler objects

The recommended code-first entry point is an IDE-guided
`WorkflowDefinition<TRequest,TResult>` base class with an overridable `Build` method. A workflow
definition object is a short-lived authoring/compiler object, not a workflow instance and not a
container of activity CLR objects.

`Build` runs only while compiling or publishing a workflow definition version. Its result is
normalized, validated, fingerprinted, and serialized as an immutable executable artifact that can
start many workflow instances. Definition-time services may contribute composition, but their
contribution must be fully materialized into the artifact. Equivalent build inputs must produce the
same behavioral artifact fingerprint; changed configuration-derived behavior produces a new
artifact.

Workflow requests are atomic typed invocation documents whose members are exposed as immutable typed
builder sources. Workflow results are atomic typed completion documents bound on every successful
terminal path. Child workflows exchange values only through their typed request and result and never
share variables implicitly.

Ordinary builder extension methods are the primary source-level reuse mechanism. A separately
versioned reusable runtime component is a child workflow, not a new fragment object model.

Code-first, visual, and API/JSON authoring must compile to exactly the same canonical executable model before
execution. Generated types and callbacks never survive into that artifact.

### 9. Put persistence policy on value owners

Persistence, external-payload storage, encryption, sensitivity, and diagnostic-redaction policy
belong to the input/result schema or variable declaration that owns the value. They do not belong to
a generic memory container. Value flow must not silently downgrade a source's required protection;
publication either propagates the effective policy or rejects an unsafe destination.

The existing durable-value persistence seam may continue to provide serialized inline/external
payload storage, but it is an implementation boundary beneath role-owned invocation, result,
variable, state, and trigger records. It is not a public workflow value-reference model.

This preserves spec 081's shared alias-based `TypeReference` and collection-shape decisions while
superseding any implication that workflow inputs, outputs, variables, and activity I/O are one
semantic argument role. Dynamic `Any` values continue to follow ADR 0036's JSON representation.

### 10. Terminate Elsa 3 memory semantics at import

Elsa 3 memory shapes are translated one-way at the importer boundary into canonical bindings,
variables, result projections, and migration state. `IMemoryBlock`, `IMemoryBlockReference`, and
related memory-register APIs are removed from canonical packages once their consumers migrate.

The importer builds an importer-local reference table before lowering. Output-only memory references
and combined expression/reference values must resolve into producer-result and consumer-binding
relationships. Dangling, cross-subtree, ambiguous, and custom references produce path-specific
diagnostics. Import never materializes a canonical memory block.

No obsolete forwarding shim remains in canonical packages. If an Elsa 3 construct cannot be mapped,
the importer emits a precise migration diagnostic. Any unavoidable temporary execution adapter lives
in an explicitly named compatibility module with a retirement plan, consistent with constitution
§E2.7.

## Reconciliation with existing work units

This ADR supersedes only the memory-shaped mechanism in earlier work. Their still-valid architectural
objectives remain requirements for the migration.

| Existing decision or specification | Preserved | Superseded or revised |
| --- | --- | --- |
| [Spec 006](../../specs/006-activity-construction-seam/spec.md), activity construction seam | Descriptor opacity, constructor registry, and DI activation | `InputArgument`/`OutputArgument` construction, wrapper binding, and workflow-value bags applied during construction |
| [Spec 011](../../specs/011-runtime-value-binding-contract/spec.md), runtime value binding | Typed compiled bindings, deterministic resolution, and no history reads | Universal reference sources and concrete producer-execution identity in the executable; result lookup becomes structural and causal |
| [Spec 207](../../specs/207-workflow-execution-slice/spec.md), workflow execution slice | Runtime invocation orchestration | The requirement that activity properties use `InputArgument<T>` |
| [Spec 029](../../specs/029-runtime-activity-invocation-boundary/spec.md), invocation boundary | Materialization before invocation and Design-free execution | `RuntimeMaterializedActivityInput.Argument` and seeding values into execution-local memory |
| [Specs 060](../../specs/060-runtime-activity-output-capture/spec.md) and [061](../../specs/061-runtime-activity-input-resolution/spec.md), output capture and input resolution | Durable availability, same-scope consumption, and deterministic failure | Independently writable output slots and execution-local memory references |
| [Spec 081](../../specs/081-typed-argument-model/spec.md), typed argument model | Alias-based `TypeReference`, collection shape, and shared type representation | Semantic unification of inputs, outputs, and variables; runtime `Variable<T>` is no longer memory-backed, and compiled metadata cannot fall back to assembly-qualified CLR names |
| [Spec 083](../../specs/083-runtime-execution-expression-carrier/spec.md) and [ADR 0030](0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md), expression carrier | Parameter-threaded evaluator infrastructure and no DI-registered live execution context | Ambient activity-output discovery, JavaScript variable write-back, mutable workflow-value APIs, and service location from the value context |
| [Spec 090](../../specs/090-activity-input-editor-options/spec.md), activity input editor options | Input editor and option metadata | Scanner examples and contracts that require `InputArgument<T>` properties |

Spec 014's authored-definition-only Elsa 3 import boundary, ADR 0020's atomic checkpoint commit,
ADR 0029's execution pipeline, and ADR 0043's publication/start authority remain unchanged. Invocation
input, state, completion, and variable-frame changes fold into the existing checkpoint commit path;
this decision does not create a second persistence route or a special code-first executor. Typed
resume triggers do not redefine start authority or provider recognition.

## DI activation-scope decision (resolved 2026-07-16)

Every CLR activity execution attempt owns one fresh child `AsyncServiceScope`. Constructor-injected
scoped and transitive dependencies are unique to that attempt, and both the activity lease and child
scope are disposed when the attempt ends or activation fails. A retry or resume retains its logical
invocation and pinned inputs but receives a new activity object, attempt identity, scope, and service
graph. Engine intrinsics execute without CLR activation or an activity scope.

The prototype compared burst-only, per-attempt, and an explicit-allowlist conditional strategy over
nine workloads. All strategies created fresh activity objects and transient dependencies. Burst-only
was rejected by the semantic gate because attempts in one burst shared scoped and transitively scoped
dependencies. The conditional candidate was semantically safe only for explicitly audited,
parameterless, non-disposable activities; every service-bearing activity fell back to per-attempt.
That candidate was rejected because it creates a second observable lifetime/audit mechanism and did
not demonstrate a stable fast-path advantage.

Representative measurements from the retained run are shown below. Each benchmark operation contains
the workload's full attempt set, so these are comparative workload figures rather than single-scope
construction timings.

| Workload | Burst-only p50 / p95 / allocated | Per-attempt p50 / p95 / allocated | Conditional p50 / p95 / allocated |
| --- | ---: | ---: | ---: |
| No-op (32 attempts) | 131.348 / 153.623 μs / 74,768 B | 177.855 / 375.708 μs / 80,144 B | 590.307 / 1,084.492 μs / 75,800 B |
| Scoped + transitive disposable (32 attempts) | 219.800 / 342.444 μs / 88,296 B | 167.975 / 426.352 μs / 104,584 B | 891.163 / 1,233.845 μs / 104,592 B |
| Mixed intrinsic/activity (128 operations) | 10.283 / 11.074 μs / 21,624 B | 47.118 / 94.594 μs / 24,312 B | 55.923 / 116.168 μs / 22,144 B |
| Retry (2 attempts) | 3.360 / 3.844 μs / 5,448 B | 51.792 / 99.024 μs / 5,784 B | 5.639 / 8.212 μs / 5,792 B |
| Concurrent drain (32 attempts) | 415.681 / 1,133.462 μs / 89,256 B | 711.186 / 1,232.076 μs / 105,192 B | 268.739 / 489.205 μs / 105,208 B |

The full table, environment, raw iteration log, throughput, and all nine workloads are retained in
[the 2026-07-16 Apple M2/.NET 10 benchmark results](../reports/evidence/095-activation-scope-benchmark/2026-07-16-m2-net10/README.md).
The run used BenchmarkDotNet 0.15.8, .NET SDK 10.0.300, .NET 10.0.8 Arm64, one launch, three warmups,
and twelve measured iterations. macOS denied the optional high-priority request; the complete run was
otherwise isolated from repository builds. Variance makes the microtimings directional, not a claim
of nanosecond precision. The semantic isolation/disposal gates therefore decide first, with the
measurements showing that their allocation cost remains bounded and intrinsics pay no scope cost.

Request-affine transports follow the same boundary: for example, synchronous HTTP delivery consumes
the committed typed `HttpResponseInstruction` after the inline drain instead of injecting an
`HttpContext` or request-scoped sink into the activity attempt.

## Alternatives considered

### Keep the Elsa 3 memory model

Rejected. It duplicates the runtime binding/durable-state substrate and treats unrelated value roles
as addressable mutable locations.

### Introduce one public `ValueSource<T>` or runtime value reference

Rejected. It makes APIs accept roles they do not semantically support and pushes source-kind checks,
mutation rules, serialization, and availability validation into a universal abstraction.

### Use a WWF-style activity CLR object graph as the workflow blueprint

Rejected. It prevents constructor-injected transient activity activation and preserves the ambiguity
between an authored activity object and a runtime invocation.

### Give activities one immutable `Inputs` record

Rejected for activity authoring. Individually annotated properties provide better binding, default,
editor, and validation metadata. Typed request/result symmetry is retained at the workflow boundary,
where it represents an external operation contract.

### Require a static workflow `Build` convention

Rejected as the primary API. It is architecturally simple but requires developers to memorize a
signature. The abstract definition base provides IDE completion and override scaffolding without
making the definition object runtime state.

### Use generated configurator chains instead of activity method calls

Rejected as the preferred facade. The method-call experience is worth a tightly contained
authoring-only carrier. The explicit foundational builder remains available for infrastructure and
dynamic authoring.

### Treat every registered function as possibly effectful (no function metadata)

Considered for the §5 amendment (D-6) and not chosen. With no metadata at all, the durability boundary of input
evaluation would have to treat every registered .NET function as possibly effectful. Every step whose inputs call
any function, including built-ins such as `newGuid()`, would then pay two commits, with no way to opt out, although
most functions called from expressions have no side effects. The boundary would still not make an effect
exactly-once, so the designer needs idempotency either way. An optional effectful declaration keeps the default
cheap and gives the commit-around-effects guarantee a consumer: it is what turns the guarantee on for a function.

### Keep obsolete memory interfaces as forwarding shims

Rejected. Shims would allow new canonical code to keep depending on the old model and spread
bidirectional conversions through the runtime.

## Consequences

- Activity authoring becomes ordinary transient CLR code with constructor injection and materialized
  input properties.
- Workflow composition gains a typed, generated method-call experience while retaining one canonical
  serialized artifact model.
- Durable values are stored by the invocation, result, variable scope, activity state, or trigger
  record that owns their semantics.
- Runtime validation becomes stricter: missing causal outputs, unsafe parallel writes, unbound
  required inputs, expressions that change engine-owned state, nonpersistable durable values, and
  activity-contract mismatch fail explicitly. (Since the §5 amendment, nondeterministic expressions
  are no longer refused.)
- Existing activity argument wrappers, expression memory APIs, runtime input seeding, output
  publication, JavaScript variable write-back, tests, and Elsa 3 importer mappings require migration.
- The concrete removal map includes `IMemoryBlock`, `IMemoryBlockReference`, `IMemoryRegister`,
  `IMemoryBlockReferenceFactory`, `MemoryBlockReference<T>`, memory-backed `Variable<T>` behavior,
  `VariableBlockMetadata`, delegate expressions and their converters, runtime memory
  seeding/publishing, and `IActivity.SyntheticProperties` wherever it acts as a hidden workflow-value
  channel.
- Framework §2.21.1 still applies: migration preserves existing test objectives, and planning records
  an explicit replacement/removal ledger for tests whose old memory-block subject disappears.
- Specs 011, 060, and 061 remain useful implementation evidence but their active-output/capture model
  must be reconciled with atomic invocation results and causal result projections. The
  container-target output-capture subset is reconciled by
  [ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md);
  the independently writable output-slot and execution-local memory mechanisms remain superseded.
- ADR 0027 remains the lexical-variable foundation, refined here with graph-visible writes and a
  deterministic concurrent-write rule.
- ADR 0031 remains the locality optimization, constrained here so burst memory cannot become workflow
  value truth.
- ADR 0035's alias-only serialization and ADR 0038's behavioral artifact fingerprint remain required
  by activity contract schemas and compiled bindings.

## Follow-up

- [Spec 095: Replace Memory-Block Value Flow](../../specs/095-value-flow-redesign/spec.md)
- Activation-scope prototype and benchmark, followed by a focused scope-lifetime decision
- Speckit plan and phased migration tasks; no runtime implementation is authorized by this ADR alone
- Import fixtures that prove Elsa 3 memory references terminate at the adapter boundary
- Architecture tests preventing canonical/runtime references to memory interfaces and
  `ActivityArgument<T>`
- Reconciliation of ADR 0030's ambient JavaScript write-back with the new pure-binding-expression
  contract
- Reconciliation of specs 011/060/061 with invocation-owned atomic results; ADR 0048 resolves the
  container-target output-capture subset, while the remaining superseded mechanisms stay excluded
