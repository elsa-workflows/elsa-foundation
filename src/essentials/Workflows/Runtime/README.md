# Workflow Runtime

`Elsa.Workflows.Runtime` owns execution of published `WorkflowExecutable` artifacts: actor serialization,
checkpointing, durable runtime state, scheduling, recovery, and runtime alterations. It does not load Design
definitions at execution time.

## Runtime alterations

A runtime alteration is a durable, authenticated plan over a sealed set of workflow executions. Plans capture all
eligible targets before a job can run, then execute the ordered alteration envelopes for each target through that
workflow's actor. Every target job either stages all effects and commits them with its terminal evidence in one
mandatory Runtime checkpoint, or stores one failed outcome and skips all non-applied envelopes. Delivery is
at-least-once; deterministic checkpoint IDs reconcile a lost acknowledgement without replaying an applied change.

The built-in descriptors are `CancelWorkflow/1`, `ModifyVariable/1`, `ScheduleActivity/1`,
`RescheduleActivity/1`, and `Migrate/1`. `CancelWorkflow` must be the sole envelope. Scheduling requires an
activity-owned compiled scheduling capability; migration is limited to a quiescent suspended execution and an exact
compatible executable artifact. Alterations never recover a workflow or resolve an incident implicitly.

Deferred envelopes are encrypted before plan persistence. Plan and job reads contain stable descriptor identity,
ordinal, status, bounded safe code/message, and structural IDs only—never an envelope payload, variable value,
exception, stack trace, secret, or handler CLR type.

## Adding a trusted custom alteration

Trusted host modules may contribute a scoped handler during composition:

```csharp
services.AddWorkflowAlterationHandler<NormalizeCustomerHandler>(
    new WorkflowAlterationDescriptor("Contoso.NormalizeCustomer", 1, "Normalize customer"));
```

The kind must be dotted/namespaced and versioned exactly. Runtime-owned built-in names are reserved. A handler must
implement `IWorkflowAlterationPreflightHandler`; it validates only its `JsonElement` payload, updates the supplied
`IWorkflowAlterationProjectedState` for later envelopes, and stages deterministic
`IWorkflowAlterationRuntimeCheckpointStagedChange` instances. It must not persist, send messages, dispatch work, or
perform an external side effect during preflight. The checkpoint collaborator applies staged Runtime changes only
after complete preflight succeeds.

`IWorkflowAlterationRegistry` offers descriptor-only host discovery, sorted by kind/version. It never constructs a
handler, exposes an executable schema type, or contributes handler CLR identity to a plan. The startup registry and
resolver dispatch only an exact `(kind, schemaVersion)` pair; duplicate pairs fail startup and there is no
latest-version fallback.

## Values that require encryption

Phase 0 of workflow secret safety (spec 188, FR-010) adds no encryption at rest to the runtime. Publication keeps a
value whose effective policy requires encryption out of an executable. Three runtime layers stand behind it for an
artifact that skipped publication, such as a runtime artifact imported without publishing it. Producer withholding
covers activity input materialization, intrinsic value writes, activity completion results and output captures into
workflow variables: it records a marker in place of the value before any inline or external storage decision. The
readers listed below cover the marker: each needs the value and refuses the marker with `VF-ACT-010`. The
checkpoint-commit backstop covers the envelopes a workflow or activity execution state carries, and the durable
values marked as requiring encryption. It refuses a commit that still carries such a value there, whichever producer
wrote it. None of the three covers bookmark payloads, outbox payloads or inspection
projections; the [withheld-values contract](../../../../specs/188-workflow-secret-safety/contracts/withheld-values-and-masking.md#known-gaps)
lists what remains open.

Publication refuses, before anything runs:

- `VF-ACT-011`: an input whose effective policy requires encryption (a credential input, for example) bound to anything
  but a secret reference or nothing, because a literal or an expression would have to be persisted before the activity
  runs.
- `VF-ACT-012`: a secret reference on an intrinsic node, on a node whose activity is not activated as a CLR activity
  (a graph activity, for example) or whose CLR type cannot be resolved, on a checkpoint participant, on an input the
  activity names in `[RefusesSecretBinding]` or `[ActivityValueOutcomes]`, or as a variable's initial value, because
  each of those would read or store the value outside activation, or cannot be checked.
- `VF-COER-001`: a secret reference on an input that the conversion rules cannot convert text into. Only a single
  `String` input and a single input declared with a canonical any alias accept one.

At run time:

- **Producer withholding.** Input materialization and intrinsic value writes share one destination-storage decision,
  `RuntimeExternalEnvelopeStorage`, and the activity completion projector decides where a completion result is stored.
  Both apply one rule, `RuntimeEncryptionWithholding`, first: it replaces a present value whose policy requires
  encryption with a withheld envelope of kind `PolicyRequiresEncryption`, which holds no value and is written neither
  inline nor to external payload storage. The value cannot be recovered. A secret read is withheld earlier, as a
  `SecretReference` marker that only activation resolves. An activity's output capture into a workflow variable
  writes the marker into the root variable frame, as `Set` does, when the captured result is withheld or is present
  with a policy that requires encryption, and the activity completes; no storage driver sees the value. The predicate
  all of these share is `ValueEnvelope.HoldsValueRequiringEncryption` (`Elsa.Workflows.Runtime.Core`).
- **`VF-ACT-010` behind `VF-ACT-011`.** A reader that needs a withheld value refuses it with `VF-ACT-010` instead of
  reading it as null. Among them are CLR activation, the `Control` intrinsic's outcome, the `SetCorrelationId` and
  `SetInstanceName` intrinsics, `SetOutput`, and an activity's output capture into a durable value row (a workflow
  output, for example), because a durable output has no withheld form. `Set` and an activity's output capture into a
  workflow variable keep the marker in the variable, and `Return`, or an activity whose result was withheld, keeps it
  as its completion result; the input-binding and expression-parameter readers of a variable or an activity result
  then refuse it with `VF-ACT-010`.
- **The checkpoint-commit backstop.** `RuntimeCheckpointCommitValidator` refuses, with
  `RuntimeCheckpointCommitValidationException` and `VF-ACT-005`, a commit that carries a present inline or external
  value whose policy requires encryption: in a workflow's root variable frame, in an activity execution's input
  snapshot, private state, completion result, trigger deliveries, variable frames and iteration frame request, or in a
  durable value whose metadata marks it as requiring encryption. The message names the state and the value's key,
  never the value. It cannot judge an inspection projection, which carries captured payloads without a policy. A
  replacement `IRuntimeActivityInputMaterializer` that stores such a value in an input snapshot has its commit refused
  here.

The run inspector and execution evidence render a withheld value by its marker; see the
[Runtime API](Api/README.md#withheld-and-protected-values) and
[execution evidence](../ExecutionEvidence/README.md#value-dispositions) documentation.

## Composition

Call `AddWorkflowRuntime()` for the host-agnostic runtime. It supplies in-memory development stores and a
process-local payload key only. A durable host must compose a durable alteration store and configure a retained
AES-256 key ring before admitting plans that must survive restart. See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for
replaceable contracts.
