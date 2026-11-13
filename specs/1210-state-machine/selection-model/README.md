# Selection reference model

Executable engineering research for [M0/Q1](../selection-model-plan.md), implementing the bounded part of the
[selection proposal](../contracts/selection.md). No product project imports it, and it defines no public schema.

From the repository root, with Node 22 or later and no package installation:

```sh
node --test specs/1210-state-machine/selection-model/selection.test.mjs
node specs/1210-state-machine/selection-model/negative-controls.mjs
```

The tests cover candidate discovery, guards, explicit order, compound/parallel exit and entry sets, source-visit
conflicts and phase dependency plans. Negative controls mutate temporary copies and require named tests to fail
when hierarchy suppression, source-visit conflict detection or the exit/action barrier is removed. The reviewed
source is never changed by the controls.

The model receives one already-grouped semantic input with a matching key in `kind`. It does not discover
subscriptions or causal cohorts. All active visits are assumed to have completed entry and to be ready for this
evaluation. Guard callbacks are synchronous and stand in for expression evaluation; they do not implement Elsa
value scopes, activity faults or durable recovery. Phase steps denote structural obligations, not newly created
Elsa activities. They are returned, never executed. Entering states have no visit occurrence yet; the actual
runtime must allocate and persist fresh occurrences. JSON-copying test input is not a durable restart test.

Ancestor-target edges are deliberately rejected: their visit-preservation semantics remain an open contract
question. This limitation is not a reduction of required product scope. Automatic/completion/business-input
arbitration, trigger rearming, receipt/admission persistence, ordinary activity execution, loop recovery,
cancellation, native compensation and Studio are outside this model. Passing tests cannot close Q1 or M0, select
native/model placement or approve the kernel design.
