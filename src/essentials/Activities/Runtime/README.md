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
registers canonical aliases in `IWellKnownTypeRegistry`. It runs on every shell (re)build and registers from the
assemblies of the features the shell was composed from first, then from the `IFeatureAssemblyProvider` assemblies, then
from the runtime-loaded ones, leaving out every other loaded assembly that has the name of a shell feature assembly.
`ActivitiesRuntimeFeature` also contributes the invoke, parent-completion, and resume scheduler handlers.

Activity kinds do not register descriptor constructors, factories, argument wrappers, memory blocks, or
mutable output publishers. CLR activation is supplied by `Elsa.Activities.Primitives`; non-CLR executable
kinds require their own explicit compile-time/runtime boundary.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for supported contributor contracts.

## Activity versions after an in-place upgrade

A package-loaded activity that is upgraded in place (a new package version in the host's feed, a shell reload, no process
restart) leaves its previous release loaded, so two assemblies declare the same activity type. The reloaded shell
registers the class of the release it composes, and an earlier release of an assembly that declares one of the shell's
features claims no alias at all, not even one the new release dropped.

A workflow node pinned to an earlier catalog version keeps running after the upgrade, but on the newest loaded class. The
activity's alias is its CLR type name, which carries no version, and one type is registered per alias: loading several
versions of one CLR type side by side is out of scope
([spec 004](../../../../specs/004-activity-semantic-versioning/spec.md)). A node pinned to `1.0.0` therefore constructs
the `1.1.0` class, hydrated from the node's bindings, which name only the inputs `1.0.0` declared; an input added since
keeps its default.

The consequence for an activity author: a release must stay input-compatible with every version workflows are pinned
to. Adding an optional input is safe. Renaming or removing an input, or changing its type or meaning, breaks every
workflow pinned to an earlier version, and nothing warns of that before such a workflow runs: a renamed or removed input
then faults the node, since the class has no input property for a key its pinned version declares, and a changed type
or meaning may not fault at all. The Add note sample (`samples/Elsa.Samples.Nuplane.Notes.Activities`) is built to that
rule.
