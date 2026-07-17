# Workflow Value Flow Uses Role-Owned Bindings And Immutable Invocation Records

Status: accepted (2026-07-16; ratified through an architect grilling session)

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

### 3. Materialize and pin inputs before activation

For durable execution, the runtime evaluates an invocation's bindings, validates type and
persistability, and commits the invocation identity plus immutable input snapshot before constructing
or invoking user activity code. Retries and resumptions retain one stable logical `InvocationId` and
the same input snapshot while receiving a new `AttemptId` and a fresh CLR activity object.

Successful completion commits one typed result payload and one authored control outcome before any
downstream work is scheduled. Named outputs are typed projections from that atomic result; they are
not independently writable output slots. A committed invocation is not re-executed merely because
downstream scheduling was interrupted.

Nonpersistable input, state, or result values are legal only under an explicit transient execution
policy. Such execution cannot suspend, migrate, or depend on durable retry. Reconstructible
burst-local caches from [ADR 0031](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)
remain optimizations and are not workflow value sources.

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
closures, discover workflow values through ambient name-based accessors, mutate variables, or expose
mutating host functions.

Stateful scripts are activities that receive inputs and return typed results. Their results can feed
an explicit `Set` operation. This decision supersedes the canonical-model portion of ADR 0030 that
kept JavaScript variable write-back and ambient output accessors: those surfaces may remain only as
isolated migration compatibility while the new model is introduced.

Time, randomness, and other nondeterministic data enter an expression as explicit pinned parameters
or through a separately specified deterministic runtime capability. Merely marking an expression
read-only is not enough if its evaluator can reach mutable ambient state.

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
variable-write mechanism. The activity invocation contract returns that transition directly; it does
not publish successful values or outcomes through mutable execution-context `Set` methods.

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
| [Spec 015](../../specs/015-workflow-execution-slice/spec.md), workflow execution slice | Runtime invocation orchestration | The requirement that activity properties use `InputArgument<T>` |
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
[the 2026-07-16 Apple M2/.NET 10 benchmark results](../../benchmarks/Elsa/Activities/Runtime/Benchmarks/results/2026-07-16-m2-net10/README.md).
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
  required inputs, impure expressions, nonpersistable durable values, and activity-contract mismatch
  fail explicitly.
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
  must be reconciled with atomic invocation results and causal result projections.
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
- Reconciliation of specs 011/060/061 with invocation-owned atomic results
