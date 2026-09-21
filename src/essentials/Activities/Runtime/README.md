# Elsa.Activities.Runtime

Runtime orchestration for transient, typed activity invocations. It owns input snapshot materialization,
one-time property hydration, activation leases, transition handling, completion projection, and CLR type
discovery. It carries no `Elsa.*.Design.*` dependency.

An executable node contains an immutable `ActivityContract` and canonical input bindings. On first invoke,
the runtime materializes and checkpoints one `ActivityInputSnapshot`. `IActivityActivator` then creates a
fresh activity in an owned DI scope and `ActivityInputHydrator` assigns its plain `[ActivityInput]`
properties exactly once. The activity returns one closed `ActivityTransition<TResult>`; successful results
are projected and committed atomically.

`RegisterActivityTypesStartupTask` discovers CLR activity types plus their annotated input/result types and
registers canonical aliases in `IWellKnownTypeRegistry`. `ActivitiesRuntimeFeature` also contributes the
invoke, parent-completion, and resume scheduler handlers.

Activity kinds do not register descriptor constructors, factories, argument wrappers, memory blocks, or
mutable output publishers. CLR activation is supplied by `Elsa.Activities.Primitives`; non-CLR executable
kinds require their own explicit compile-time/runtime boundary.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for supported contributor contracts.
