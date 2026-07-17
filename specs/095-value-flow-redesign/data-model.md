# Data Model: Role-Owned Workflow Value Flow

This document defines the persisted and canonical entities for [spec 095](spec.md). It preserves the
constitutional authored-state → executable-artifact → runtime-state split. Names describe semantic
records; illustrative CLR spellings may change without changing their ownership or invariants.

## Model boundaries

| Layer | Source of truth | May contain | Must not contain |
| --- | --- | --- | --- |
| Authored | `WorkflowDefinitionState` and versioned activity catalog rows | User intent, stable member keys, expressions, variables, structure, editor metadata | Runtime execution IDs, materialized invocation values, CLR activity objects |
| Executable | `WorkflowExecutable` | Normalized bindings, pinned activity contracts, result projections, scope/control structure | Design entities, generated authoring carriers, delegates, assembly-qualified CLR names |
| Runtime | Workflow, activity, frame, state, trigger, and completion records | Materialized persistable values and execution identity | Authored expression ambiguity, memory addresses, mutable activity objects, service instances |

Only Publishing crosses from Authored to Executable. Runtime consumes the executable and configured
runtime capabilities; it does not load Design state. Elsa 3 shapes cross only through the one-way
importer into Authored state.

## Shared value types

### `ValueTypeDescriptor`

Portable identity for a value type.

| Field | Meaning |
| --- | --- |
| `Alias` | Stable alias registered for the scalar or element type |
| `CollectionShape` | Scalar, nullable, list, set, dictionary, or another registered structural shape |
| `Schema` | Optional immutable JSON schema or registered schema descriptor |
| `SchemaVersion` | Optional version of the schema contract |

Invariants:

- `Alias` is required and resolves through the alias registry at publication preflight.
- Assembly-qualified names, CLR assembly versions, and `Type.GetType` fallback strings are forbidden in
  authored, executable, and persisted runtime records.
- Dynamic `Any` uses the registered `Any` alias and JSON representation. It does not persist arbitrary
  CLR object graphs.
- Assignability is determined from registered aliases, collection shape, and schema compatibility, not
  from opportunistic runtime reflection.

### `ValueEnvelope`

Materialized representation used inside role-owned runtime records.

| Field | Meaning |
| --- | --- |
| `Type` | `ValueTypeDescriptor` for the materialized value |
| `Presence` | `Present`, `ExplicitNull`, or `Absent` where the owning role permits absence |
| `InlinePayload` | Canonical JSON payload when stored inline |
| `ExternalReference` | External payload locator when the owner's policy selects external storage |
| `Policy` | Effective persistence, sensitivity, encryption, retention, and redaction policy |

Exactly one payload location is used for a present value. `ExplicitNull` is distinct from `Absent` and
has no external locator. An envelope never supplies identity or mutability by itself; the containing
request, snapshot, result, variable frame, state, or trigger record owns those semantics.

## Authored entities

### `WorkflowDefinitionState`

Existing immutable authored content for one workflow definition version.

Relevant fields:

- typed workflow request definition;
- typed successful workflow result definition;
- root activity tree and structured child regions;
- workflow-level variable declarations;
- authored input bindings and explicit control-flow connections;
- authored expression definitions.

Code-first, visual, and API/JSON authoring all produce this entity. A
`WorkflowDefinition<TRequest,TResult>` object is a compiler object that creates this state; it is not
persisted as a workflow instance or executable object graph.

### `ActivityDefinitionVersion`

Versioned catalog contract discovered from generated metadata, manual metadata, or reflection.

| Field | Meaning |
| --- | --- |
| `ActivityTypeKey` | Stable activity type identity |
| `ContractVersion` | Author-controlled compatible contract version |
| `SchemaFingerprint` | Hash of behaviorally relevant input/result/outcome/activation metadata |
| `Inputs` | Ordered `ActivityInputDefinition` collection |
| `Result` | One `ActivityResultDefinition`, including named projections |
| `Outcomes` | Stable successful control outcome keys |
| `ActivationRequirement` | Descriptor kind and required constructor/service capability metadata |
| `DesignFacets` | Editor and structural metadata not needed as runtime state |

Generated, manually supplied, and reflection-discovered descriptions of the same activity contract
must normalize to equivalent values for every behaviorally relevant field.

### `ActivityInputDefinition`

Definition of one independently bindable plain `[Input]` property.

| Field | Meaning |
| --- | --- |
| `Key` | Stable, case-sensitive contract key; independent of CLR property name |
| `Name` | Current author/editor display name |
| `Type` | Portable value type descriptor |
| `Required` | Whether omission is invalid |
| `Default` | Optional pinned default `ValueEnvelope` |
| `Policy` | Minimum value-protection policy |
| `EditorMetadata` | Ordering, category, UI hint, options, and provider metadata |

An omitted binding, explicit null, and a literal default are distinct authored states. Publication
must reject omission for a required input, lower it to the pinned literal default when one exists,
or preserve accepted optional omission as an `Absent` literal for nullable targets. A non-nullable
input without a binding or pinned default is invalid. The executable and complete invocation snapshot
remain authoritative; hydration deterministically maps optional `Absent` to null instead of consulting
transient CLR property initializers.

### `ActivityResultDefinition`

Definition of the one atomic successful result returned by an activity.

| Field | Meaning |
| --- | --- |
| `Type` | Type of the whole result document |
| `Required` | Whether successful completion permits an explicit null result |
| `Policy` | Minimum protection policy for the complete result |
| `Projections` | Named `ActivityResultProjectionDefinition` collection |

### `ActivityResultProjectionDefinition`

Read-only view over a member of the complete result.

| Field | Meaning |
| --- | --- |
| `Key` | Stable projection key used by authored consumers |
| `Path` | Deterministic member/JSON path within the result document |
| `Type` | Portable projected type |
| `Required` | Whether the projection may be null or absent |
| `Policy` | Additional protection required by the projection, if any |

Projections are never independently assigned output slots.

### `VariableDefinition`

Authored declaration handle for explicitly mutable lexical state.

| Field | Meaning |
| --- | --- |
| `Key` | Stable declaration key |
| `Name` | Display/source name |
| `Type` | Portable value type |
| `DeclaringScopeId` | Structured region that owns the declaration |
| `InitialBinding` | Optional binding materialized when a frame activates |
| `Policy` | Persistence and protection policy |

`Variable<T>` in the builder is a typed handle to this definition, not runtime storage.

### `ExpressionDefinition`

Authored portable function definition.

| Field | Meaning |
| --- | --- |
| `Language` | Registered evaluator language, such as JavaScript or Liquid |
| `Source` | Serialized expression source |
| `ResultType` | Expected portable result type |
| `Parameters` | Ordered named parameters, each with an authored binding |
| `Options` | Serializable evaluator options |
| `CapabilityProfile` | Explicit deterministic capabilities permitted during evaluation |

Binding expressions cannot carry delegates, closures, ambient variable/output names, mutating host
functions, or implicit time/random/configuration access.

## Executable entities

### `WorkflowExecutable`

Immutable runnable artifact and the sole Design-to-Runtime seam.

Relevant fields:

- artifact format version and behavioral identity;
- typed workflow request and result schemas;
- executable node graph and structured scope table;
- required runtime capability manifest;
- publication and compatibility metadata.

Publication succeeds only when all contract aliases, schema fingerprints, activation requirements,
expression evaluators, and required result paths are valid. An executable cannot make missing activity
types or required services an expected invocation result.

### `ExecutableNode`

One immutable placement of an activity contract or engine intrinsic.

| Field | Meaning |
| --- | --- |
| `NodeId` | Stable identity within the executable graph |
| `ActivityContract` | `ActivityContractPin` for a CLR activity, absent for an intrinsic |
| `IntrinsicKind` | Set/return/merge/control operation interpreted by the engine |
| `InputBindings` | Exactly one normalized binding per input key |
| `ResultContract` | Whole result schema and named projection table |
| `Outcomes` | Stable successful control outcomes |
| `ScopeId` | Lexical structured region containing the node |
| `Children` / `Edges` | Structured child slots and causal control connections |

Exactly one of `ActivityContract` and `IntrinsicKind` is present. Intrinsic nodes do not create a CLR
activity activation or activity DI scope.

### `ActivityContractPin`

Executable copy of the behavior needed to activate and validate an activity without Design access.

| Field | Meaning |
| --- | --- |
| `ActivityTypeKey` | Stable activity type identity |
| `ContractVersion` | Pinned contract version |
| `SchemaFingerprint` | Pinned complete contract fingerprint |
| `DescriptorKind` / `DescriptorPayload` | Opaque runtime activation descriptor |
| `Inputs` | Stable input keys, types, requiredness, defaults, and policies |
| `Result` | Whole result and projection schema |
| `Outcomes` | Successful authored outcomes |
| `ActivationRequirement` | Required registered activation capability |

CLR member names may change while stable keys and compatible schema remain unchanged. An incompatible
schema requires a new contract version and artifact.

### `RuntimeInputBinding`

Closed discriminated union describing one legal source for one activity input.

Common fields: `InputKey`, `TargetType`, `EffectivePolicy`, and the selected source payload.

| Kind | Payload |
| --- | --- |
| `Literal` | Complete literal `ValueEnvelope` |
| `WorkflowRequest` | Stable request-member key/path |
| `VariableRead` | Stable variable key and declaring scope ID |
| `ActivityResult` | Producer node ID and stable result projection key |
| `Expression` | `RuntimeExpressionBinding` |

The union has no address, `Get`, `Set`, memory identity, concrete producer execution ID, or generic
reference escape hatch. Required result availability and policy compatibility are publication gates.

### `RuntimeExpressionBinding`

Executable pure expression.

| Field | Meaning |
| --- | --- |
| `Language` / `Source` | Registered language and portable source |
| `ResultType` | Portable expected result type |
| `Parameters` | Name → normalized `RuntimeInputBinding` |
| `Options` | Serialized evaluator options |
| `CapabilityProfile` | Explicit deterministic capabilities |

Parameter bindings are materialized before evaluation. The evaluator receives an immutable parameter
map, never an activity execution or variable context.

### `ActivityResultBinding`

Executable binding from a consumer input to a producer's result projection.

| Field | Meaning |
| --- | --- |
| `ProducerNodeId` | Authored/executable producer node |
| `ProjectionKey` | Stable key in the producer's pinned result contract |
| `ProducerScopeId` | Structural scope used for validation |
| `Availability` | Required or explicitly optional |

At runtime the resolver selects the unique completed producer invocation visible in the consumer's
structural frame and causal scheduling lineage. It never selects the newest completion globally.

### `ExecutableScope`

Compiled lexical region.

| Field | Meaning |
| --- | --- |
| `ScopeId` | Stable executable scope identity |
| `ParentScopeId` | Lexical parent, null for the workflow root |
| `Kind` | Workflow, sequence, branch, loop, iteration, or another structured kind |
| `Variables` | Stable variable declarations owned by this scope |
| `ResultContract` | Optional structured result leaving the scope |
| `ConcurrencyPolicy` | Sequential, parallel, or deterministic merge/reduction contract |

Publication rejects potentially concurrent ordinary writes to the same visible variable unless the
scope names an explicit deterministic merge or reduction.

## Runtime entities

### `WorkflowRequestSnapshot`

Immutable typed request pinned when a workflow instance starts.

Fields: `WorkflowExecutionId`, request `Type`, complete request `ValueEnvelope`, artifact identity,
and `CapturedAt`. It is owned by the workflow execution record and cannot be mutated after the start
checkpoint.

### `ActivityExecutionState` (logical invocation)

Durable aggregate for one logical invocation of one executable node. The existing
`ActivityExecutionId` becomes the canonical `InvocationId`; serialization may retain the old field
name only through the document upcaster.

| Field | Meaning |
| --- | --- |
| `InvocationId` | Stable across retry and resumption |
| `WorkflowExecutionId` / `ExecutableNodeId` | Owning execution and pinned node |
| `ContractIdentity` | Type key, contract version, and schema fingerprint used by this invocation |
| `Status` | Scheduled, Running, Suspended, Completed, Faulted, or Cancelled |
| `SchedulingProvenance` | Parent, branch, iteration, path, scope, and causal scheduling identity |
| `InputSnapshot` | Complete immutable `ActivityInputSnapshot`, null only while Scheduled |
| `Attempts` | Ordered immutable `ActivityAttempt` records |
| `PrivateState` | Last committed `ActivityPrivateState`, if suspended/stateful |
| `Completion` | Atomic `ActivityCompletion`, present only when Completed |
| `BookmarkIds` / `IncidentIds` | Existing durable relationships |
| `DocumentVersion` | Persisted shape version used by Groundwork upcasting |

The aggregate is stored through `IActivityExecutionStateStore`. Groundwork persists it as one
versioned activity-execution document; in-memory and provider stores must preserve identical
serialization semantics.

### `ActivityInputSnapshot`

Complete materialized input set pinned before any CLR activity construction or user code.

| Field | Meaning |
| --- | --- |
| `InvocationId` | Owning logical invocation |
| `ContractFingerprint` | Contract against which values were validated |
| `BindingFingerprint` | Behavioral identity of the executable input bindings |
| `Values` | Stable input key → `ValueEnvelope` |
| `MaterializedAt` | Timestamp of the ActivityStarted checkpoint |

Invariants:

- The keys exactly equal the pinned contract's normalized input set.
- Every required key carries a non-`Absent`, type/policy-compatible value; an optional key may carry
  `Absent`.
- Durable execution accepts only persistable envelopes.
- The snapshot is created while transitioning Scheduled → Running and committed atomically with the
  first attempt and the post-commit invocation intent.
- Retry, resumption, and parent-completion callbacks hydrate only from this snapshot; they never
  reevaluate bindings.

### `ActivityAttempt`

Immutable record for one transient activation attempt.

| Field | Meaning |
| --- | --- |
| `AttemptId` | Unique within the workflow execution |
| `InvocationId` | Owning logical invocation |
| `Ordinal` | Monotonically increasing attempt number |
| `Reason` | Initial, Retry, or Resume |
| `StartedAt` / `EndedAt` | Attempt lifetime |
| `TriggerDeliveryId` | Resume delivery used by this attempt, if any |
| `TransitionKind` | Complete, Suspend, Fault, or Cancel once ended |
| `IncidentId` | Normalized fault incident link, if faulted |

An attempt owns no serialized CLR object or service. Each attempt creates a fresh activation; the
activation lease deterministically disposes any owned DI scope after the attempt ends.

### `ActivityCompletion`

Atomic successful completion for a logical invocation.

| Field | Meaning |
| --- | --- |
| `InvocationId` / `AttemptId` | Logical invocation and completing attempt |
| `ResultType` | Pinned whole-result type |
| `Result` | One complete `ValueEnvelope` |
| `OutcomeKey` | One successful authored control outcome |
| `CompletedAt` | Commit timestamp |
| `ContractFingerprint` | Contract that defines valid projections |

Completion, terminal activity status, result, outcome, inspection projection, and downstream
scheduling intent commit through the existing checkpoint path. Once committed, completion is
immutable and recovery schedules downstream work without reexecuting the activity. Named values are
read by applying executable result-projection paths to `Result`; no per-output write or durable row is
authoritative.

### `ActivityPrivateState`

Last committed immutable state document for one stateful invocation.

| Field | Meaning |
| --- | --- |
| `InvocationId` | Owning invocation |
| `StateType` / `StateSchemaVersion` | Portable state contract |
| `Value` | Complete persistable state envelope |
| `ProducedByAttemptId` | Attempt that suspended with this state |
| `CommittedAt` | Suspension checkpoint timestamp |

Suspension replaces the whole state document; activity fields and services are not state. State and
trigger registrations commit atomically with the Suspended transition.

### `TriggerRegistration` and `TriggerDelivery`

`TriggerRegistration` is the typed durable expectation associated with an activity suspension.

| Registration field | Meaning |
| --- | --- |
| `RegistrationId` | Durable bookmark/trigger identity |
| `InvocationId` / `ResumeTargetKey` | Suspended invocation and typed callback contract |
| `PayloadType` | Expected portable trigger payload type |
| `StimulusType` / `StimulusHash` | Existing provider recognition identity |
| `DeduplicationPolicy` | Duplicate handling contract |

`TriggerDelivery` contains `DeliveryId`, registration identity, typed payload envelope, provider
identity, received timestamp, and deduplication key/status. Bookmark state remains the persistence
owner for registration and consumption. Consumption, the new resume attempt, and any completion or
replacement suspension commit through the bookmark-consumption checkpoint. A duplicate or type-
incompatible delivery is rejected before activity code observes it.

### `VariableFrameState`

Runtime storage for one activation of one executable lexical scope.

| Field | Meaning |
| --- | --- |
| `FrameId` | Stable runtime frame identity |
| `ScopeId` | Executable lexical declaration scope |
| `ActivationId` | Concrete workflow/container/iteration activation |
| `ParentFrameId` | Visible lexical parent |
| `Values` | Stable variable key → `ValueEnvelope` |
| `Revision` | Monotonic concurrency/checkpoint revision |
| `Status` | Active or Closed |

The workflow root frame is owned by workflow execution state. A structured/container or iteration
frame is owned by its corresponding activity execution state. The same record shape is used at every
level, and a checkpoint may update multiple owning states atomically. The durable-value storage seam
may store inline/external payloads beneath frame values, but it does not become the variable model.

Only an executable `Set`, merge, or reduction intrinsic may change a frame. Reads occur when a
consumer snapshot materializes. Sequential downstream work is scheduled only after the changed frame
commits. Closed frames remain inspection evidence subject to retention policy and cannot be read by
new sibling activations.

### `WorkflowCompletion`

Immutable successful workflow result owned by workflow execution state. It contains the complete
typed result envelope, completing terminal identity, artifact identity, and completion timestamp.
Every successful terminal path must provide it; faults, cancellation, and suspension are not workflow
results.

### `ActivityFaultRecord`

Normalized persistable fault evidence linked from an attempt and incident record. It contains safe
failure type/code/message/stack policy, invocation/attempt identity, and timestamps. Arbitrary CLR
exception objects never become workflow values.

## State transitions and atomicity

| From | Event | To | Required atomic commit |
| --- | --- | --- | --- |
| — | Schedule node | Scheduled | New logical invocation and scheduling provenance |
| Scheduled | Start | Running | Complete input snapshot, first attempt, ActivityStarted checkpoint, post-commit invoke intent |
| Running | Complete | Completed | End attempt, atomic completion result/outcome, closed private state, inspection, downstream intent |
| Running | Suspend | Suspended | End attempt, complete private state, typed trigger registrations, bookmark intent |
| Running | Fault | Faulted | End attempt and normalized incident/fault evidence |
| Running | Cancel | Cancelled | End attempt and cancellation evidence |
| Suspended | Consume valid trigger | Running | Consume/deduplicate delivery and create resume attempt using the existing input snapshot/state |
| Faulted | Approved retry | Running | Create retry attempt using the existing input snapshot; do not rematerialize inputs |

`Completed` and `Cancelled` are terminal. A completed invocation never changes its result. A resumed or
retried invocation retains `InvocationId`, `InputSnapshot`, contract identity, and committed private
state while receiving a new `AttemptId` and CLR activation.

External side effects remain at-least-once. `InvocationId` is the stable idempotency key supplied to
activity integrations; checkpoint atomicity cannot make an arbitrary external system transactional.

## Persistence ownership

| Record | Persistence owner | Atomic path |
| --- | --- | --- |
| Authored definitions/contracts | Existing Design stores | Design save/submit commands |
| `WorkflowExecutable` | Executable store | Publication |
| Workflow request/completion/root frame | Workflow execution state | Runtime checkpoint |
| Input snapshot/attempt/private state/completion/container frame | Activity execution state document | Runtime checkpoint |
| Trigger registration/delivery consumption | Bookmark state | Create/consume bookmark checkpoint |
| Large/encrypted value payload | Existing durable/external payload seam beneath the owning record | Same owner checkpoint |
| Fault evidence | Incident state plus attempt link | Runtime fault checkpoint |

No role record is saved through an out-of-band second persistence route. Provider fallback paths must
preserve ordering and idempotency but are not allowed to weaken the canonical checkpoint contract.

## Compatibility, document versions, and upcasting

1. `WorkflowExecutable`, activity-execution documents, workflow-execution documents, bookmark
   documents, and variable-frame-containing owner documents carry explicit format versions.
2. Upcasters are ordered, deterministic, side-effect-free transformations from one persisted version
   to the next. They do not load Design state, instantiate activities, evaluate expressions, call
   services, or guess missing causal relationships.
3. An upcast succeeds only when it can construct the complete target invariant. In particular, an
   already-started legacy invocation without a pinned input snapshot cannot be deterministically
   upgraded by rereading current variables or outputs and must fail the compatibility gate.
4. A legacy completion may be upgraded only when its independently stored outputs uniquely and
   completely reconstruct the declared atomic result and outcome. Otherwise it is incompatible.
5. A legacy executable binding using assembly-qualified type metadata, concrete producer execution
   identity, ambiguous latest-output lookup, delegate expressions, or a generic reference is
   upcastable only when an exact alias-typed, structural, causal equivalent can be proven. Otherwise
   the workflow definition must be republished.
6. Mixed-version recovery tests cover every supported upcaster. Unknown future versions fail loudly;
   no best-effort dual read is permitted.
7. Elsa 3 import is not document upcasting. Elsa 3 references are resolved by an importer-local graph
   into Authored entities or path-specific diagnostics, and Elsa 3 DTOs never enter executable or
   runtime state.
8. Contract compatibility permits a CLR member rename only when stable keys, aliases, schema,
   requiredness, defaults, policies, result projections, outcomes, and activation requirements remain
   compatible. Incompatible changes require a new contract version and artifact identity.

## Explicitly removed or non-persisted concepts

The following are not canonical entities and must not appear in executable or runtime packages after
migration:

- `IMemoryBlock`, `IMemoryBlockReference`, `IMemoryRegister`,
  `IMemoryBlockReferenceFactory`, `MemoryBlockReference<T>`, and memory metadata;
- `Argument`, `InputArgument<T>`, and `OutputArgument<T>`;
- memory-backed runtime `Variable<T>` and name-addressed variable storage;
- independently writable output slots and the active-output register as semantic truth;
- runtime input memory seeding and output memory publication;
- `IActivity.SyntheticProperties` as a hidden workflow-value channel;
- a universal runtime `ValueSource<T>` or generic value-reference/address abstraction;
- ambient expression execution contexts, workflow-value service locators, mutable expression host
  functions, delegates, and captured closures;
- a persisted WWF-style CLR activity object graph or separate canonical blueprint artifact.

The following exist only transiently and are never persisted:

- CLR activity objects and their fields;
- constructor-injected services and DI scopes;
- `ActivityActivation` leases;
- code-first `WorkflowDefinition<TRequest,TResult>` compiler objects;
- generated `ActivityArgument<T>`, call handles, builder sources, and callbacks;
- evaluator-native JavaScript/Liquid objects created from immutable parameter envelopes.
