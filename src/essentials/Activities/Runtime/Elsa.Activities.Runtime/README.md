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
plan and hydrates the activity from a transient copy of the snapshot; nothing resolved is written back. Each resolved
value is registered with `IRuntimeSecretMask` for the activity execution until its work handler has recorded the
outcome. While it is registered, the invoke, resume, parent completion and parent notification handlers show
`[secret:<reference name>]` instead of the value in the text they record for that execution: the message, stack trace
and inner exception chain of an exception activation or activity code threw (an activation failure that parks the
activity is recorded as it is), the message of an `ActivityFault` the
activity returned, the incident message and its `runtime.fault*` metadata, and the aggregate a cancellation arm
reports when disposal also failed. Not masked: a fault's code, a returned fault's category and fault type, exception
type names, and what the activity writes to the console or its own logger, returns as outputs, or keeps in private
state or bookmarks (spec 188 assumptions). A
failed resolution faults the activity with `RuntimeSecretResolutionException`, which names the reference and the
failure code and carries no value; the fault records the code and whether it is retryable, also when it is masked. A host that composes no
resolver cannot resolve at all, so the activity waits with an activation-failure incident instead of faulting; a host
that composes more than one does not start, because `ActivitiesRuntimeFeature`'s startup check fails its shell
activation. A
value withheld because its policy requires encryption cannot be recovered and is refused with `VF-ACT-010`, as is
any withheld input of a strategy that does not hydrate inputs.

## Sensitive and credential inputs

An activity author declares the sensitivity of an input on its `[ActivityInput]` attribute:

```csharp
[ActivityInput(IsSensitive = true)]
public string? CustomerNote { get; set; }

[ActivityInput(IsCredential = true, DisplayName = "API key")]
public string? ApiKey { get; set; }
```

`IsSensitive` marks data that must not appear in logs, evidence or inspection output. `IsCredential` marks an input
that accepts only a secret reference or no binding; it implies `IsSensitive`. CLR reconciliation records the
declaration on the input's catalog entry (`InputDefinition.IsSensitive`, `InputDefinition.IsCredential`, null when
not declared, so an activity that declares nothing keeps its catalog hash), and the Activities Design API's authoring
catalog reports both flags for every input. Reconciliation refuses a credential declaration that could never be
bound to a secret reference: one with a `DefaultValue`, one on an input that is not a `string`, one on an input the
type names in `[RefusesSecretBinding]`, and any on a type that implements `IRuntimeActivityCheckpointParticipant`. The
Activities Design API and the JSON activity catalog refuse a credential declaration, because neither can check that
the input could be bound to a secret reference.

Publication compiles each input's effective policy as the stricter of the declaration and the author's per-binding
choice (`ArgumentState.IsSensitive`). A sensitive declaration makes every binding sensitive; a credential
declaration makes it sensitive and requires encryption. An authored `IsSensitive: false` on a declared input is a
downgrade and is refused with `VF-ACT-005`. The pinned `ActivityContract` carries the same policy for each input and
an explicit `ActivityInputContract.IsCredential` flag, which nothing infers from `RequiresEncryption`.

An input whose effective policy requires encryption accepts only a secret reference or no binding: no other value
can reach the activity without the input snapshot persisting it first. Publication refuses a literal, an object, a
variable or other value read, an expression, and the declared default of an unbound input on such an input with
`VF-ACT-011`, naming the node and the input and never the value. A binding that carries no value (an empty or null
literal) leaves the input unbound and compiles as an unbound input does. A sensitive input that is not a credential does not
require encryption, so it takes a literal or an expression, and its value is materialized like any other, marked
sensitive.

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
([spec 004](../../../../../specs/004-activity-semantic-versioning/spec.md)). A node pinned to `1.0.0` therefore constructs
the `1.1.0` class, hydrated from the node's bindings, which name only the inputs `1.0.0` declared; an input added since
keeps its default.

The consequence for an activity author: a release must stay input-compatible with every version workflows are pinned
to. Adding an optional input is safe. Renaming or removing an input, or changing its type or meaning, breaks every
workflow pinned to an earlier version, and nothing warns of that before such a workflow runs: a renamed or removed input
then faults the node, since the class has no input property for a key its pinned version declares, and a changed type
or meaning may not fault at all. The Add note sample (`samples/Elsa.Samples.Nuplane.Notes.Activities`) is built to that
rule.
