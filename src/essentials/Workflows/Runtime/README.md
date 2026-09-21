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

## Composition

Call `AddWorkflowRuntime()` for the host-agnostic runtime. It supplies in-memory development stores and a
process-local payload key only. A durable host must compose a durable alteration store and configure a retained
AES-256 key ring before admitting plans that must survive restart. See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for
replaceable contracts.
