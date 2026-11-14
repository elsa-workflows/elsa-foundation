# Scoped ordinary work and value lifetime

**Status:** proposed M0 engineering profile, 14 November 2026. This records a candidate for execution proof,
not owner agreement, product placement, a public definition schema or completed StateMachine support.
[FR-032](../spec.md) and [OD21](../acceptance-matrix.md) require machine-wide and state-visit values; normal
Elsa value, binding, durability, secret and parallel-write contracts remain authoritative.
The [selection contract](selection.md) owns transition selection and the OD22 phase order.

## Identity and lookup

The machine has one lifetime environment. Each entered state visit has its own local environment; its parent
is the nearest state ancestor's environment, or the machine environment. A state definition ID does not identify
a visit. Retained visits keep their environments and values; authored re-entry gets fresh environments/defaults.
A host restart restores the existing environments and pending work, rather than entering the states again.

Ordinary activity work uses the existing value evaluator and assignment rules. A nearer ordinary composite or
iteration declaration still shadows state values. An assignment updates the nearest declaration under those
rules; scope choice adds no stronger concurrent-write isolation or implicit copying of values between states.
An embedded machine owns its own environments; ordinary surrounding activity environments remain available
through normal outer-frame lookup after its own declarations.

## Proposed phase affiliation

| Work | Environment and lifetime |
|---|---|
| Machine initialization | Create machine declarations once for this machine invocation. |
| State Entry | Initialize the new visit immediately before its Entry step begins, including when authored Entry work is empty. Parent Entry completes before child initialization/Entry. |
| State Exit | Use the exact exiting visit, with its existing values and parent chain. |
| Transition Action | Use the recorded source visit. Keep that source environment through required Action work even if its Exit has finished. |
| Action without exit | Use the retained source visit; preserve other visit environments and defaults. |
| Target Entry | After all required Exits and Actions succeed, retire exited environments child-first, then initialize and enter targets outside-in. |
| Final Entry | Keep its environment while Entry waits or awaits the normal recovery boundary. Complete only after success; ordinary machine disposal then releases its environments. |

Action visibility after Exit is an engineering proposal. Retaining that environment does not declare its source
state active again, revive waits, or grant an action a new visit. It avoids losing source values needed by
ordinary transition work. It must be qualified explicitly and is not an additional accepted owner choice.

Target values must not initialize during selection or before Exit/Action mutations. A planned target visit
identity can be reserved earlier without initializing its values. A preserved parent is not reinitialized when
its child changes. A whole parallel-parent re-entry resets all affected visits and follows the already agreed
explicit targets/initial children. Independent regional work keeps the existing phase and hierarchy barriers.

## Durable execution and disposal

The selected set, source/exit visit identities, current/planned visit identities, environment references,
initialization/retirement markers and ordinary child progress must remain consistent in the same durable
execution state. Environment values stay with the ordinary runtime value owner; the machine must not keep a
second mutable value dictionary or a separate evaluator.

Once entered, a visit's environment is created once across child callbacks and restarts. Once retired, its
handle cannot be reused for a new visit. The selected transition's recorded old configuration can remain as
historical context while target Entry is pending; it must not be mistaken for live source environments after
retirement. Reader validation must distinguish those states and must not allocate or initialize during probing.

Before retirement, all scoped work using exited environments must have stopped or settled under normal
cancellation/ownership rules. The generic live-consumer and child-resource guards remain enabled. Completion,
cancellation and ordinary fault disposal follow the runtime's existing policies. This profile does not supply
normal incident/retry or successful-sibling preservation, does not resume a terminal Park row, and changes no
forensic/value-retention policy. Those recovery and trigger-ownership mechanisms remain mandatory integration.

## Prototype and proof boundary

The private prototype may use literal JSON declaration defaults to qualify the execution boundary. It does not
replace the normal product Variable/default-binding schema. Full product initialization, bindings, outputs,
publication/reusable activities and Studio authoring/inspection remain required.

Old checkpoints did not contain these declaration environments. A bounded reader can materialize empty legacy
environments only during real mutating execution, with provenance explicit. A pending legacy checkpoint must
not silently acquire newly introduced nonempty declarations. Reader fixtures and deliberately seeded old
checkpoints are separate from actual new-writer/reload evidence, mixed-binary or provider qualification.

Qualification must use normal activities and bindings at root and nested placements in both persistence modes:
retained versus fresh visits, parent/child shadowing, independent regions and opposite callback orders, ordinary
ForEach shadowing, nested machines, waits/restarts in every phase, source Action visibility, target initialization
timing, final Entry and disposal. Inspect committed runtime environment data and machine references independently.
Negative controls must catch wrong source affiliation, early/double initialization, resume resets, retained-parent
resets, missed retirement and premature final disposal. Source-wave closure, all ordinary trigger semantics,
normal recovery, providers, compensation and full product/Studio acceptance remain open until separately proved.
