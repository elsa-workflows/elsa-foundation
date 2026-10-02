# Elsa.Activities.Runtime

Runtime orchestration for transient, typed activity invocations. It owns input snapshot materialization,
one-time property hydration, activation leases, transition handling, completion projection, and CLR type
discovery. It carries no `Elsa.*.Design.*` dependency.

An executable node contains an immutable `ActivityContract` and canonical input bindings. On first invoke,
the runtime materializes and checkpoints one `ActivityInputSnapshot`. `IActivityActivator` then creates a
fresh activity in an owned DI scope and `ActivityInputHydrator` assigns its plain `[ActivityInput]`
properties exactly once. The activity returns one closed `ActivityTransition<TResult>`; successful results
are projected and committed atomically.

A `Secret` binding never puts a value in that snapshot: the materializer records a withheld envelope holding the
secret reference and its conversion plan from text. `ActivityActivator` resolves it, through its
`ActivitySecretInputResolver` collaborator, each time it hydrates an activity (invoke, bookmark resume, structural
parent evaluation, and the re-materialized activation after a child completes) through `IRuntimeSecretResolver`, for
the partition the execution runs under, which is the scope the instance's own rows are stored under, and refuses with `TenantMismatch`
before reading anything when the instance records a different tenant. It converts the resolved text with the envelope's
plan and hydrates the activity from a transient copy of the snapshot; nothing resolved is written back or kept. A
failed resolution faults the activity with `RuntimeSecretResolutionException`, which names the reference and the
failure code and carries no value; the fault records the code and whether it is retryable. A host that composes no
resolver cannot resolve at all, so the activity waits with an activation-failure incident instead of faulting; a host
that composes more than one does not start, because `ActivitiesRuntimeFeature`'s startup check fails its shell
activation. A
value withheld because its policy requires encryption cannot be recovered and is refused with `VF-ACT-010`, as is
any withheld input of a strategy that does not hydrate inputs.

`RegisterActivityTypesStartupTask` discovers CLR activity types plus their annotated input/result types and
registers canonical aliases in `IWellKnownTypeRegistry`. `ActivitiesRuntimeFeature` also contributes the
invoke, parent-completion, and resume scheduler handlers.

Activity kinds do not register descriptor constructors, factories, argument wrappers, memory blocks, or
mutable output publishers. CLR activation is supplied by `Elsa.Activities.Primitives`; non-CLR executable
kinds require their own explicit compile-time/runtime boundary.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for supported contributor contracts.
