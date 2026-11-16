# Execution architecture

> **Proposed.** The kernel design awaits owner approval. These references do not authorize product implementation.

These pages describe the proposed clean-slate execution kernel. They are design references, not approval to replace Elsa's current runtime. [Program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) tracks the proposal, qualification and switch. The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md) records the owner's decisions and release scope.

## Reading order

1. [Kernel walkthrough](https://vowful-mustard-pd8t.here.now/): the layers, records, turns, ownership and execution models, with interactive diagrams. [HTML source](../reports/clean-slate-kernel/walkthrough.html).
2. [The kernel in motion](https://crystal-pellet-h5m3.here.now/): follow a workflow through persistence modes, faults and the fence lab. [HTML source](../reports/clean-slate-kernel/kernel-in-motion.html).
3. [Storage model](storage-model.md) — **Proposed**: the provider contract, record lifecycles, writers, invariants, query paths, indexes and retention costs.
4. [Ownership and fencing](ownership-and-fencing.md) — **Proposed**: why leases need a fence, how take and commit interact, and the costs and alternatives.
5. [Fairness and backpressure](fairness-and-backpressure.md) — **Proposed**: admission at the edge, per-tenant limits, fair turn selection within a pass, and the bound on inbox growth.
6. [Large fan-out and fan-in](large-fan-out.md) — **Proposed**: the combiner tree with credits, its costs against a single parent, and release 1's cap on a parallel ForEach.
7. [Data lifecycle](data-lifecycle.md) — **Proposed**: where payloads live, retention and deletion of finished instances, erasure, encryption at rest, and what the journal format must fix now.
9. [Observability and operator actions](observability.md) — **Proposed**: trace context on every message, stable metric names, inspect, retry and discard of parked work, and the read models operators use.

The walkthrough's data cards link to the storage reference. Its ownership chapter links to the fencing explanation. These references explain the contract; experiment reports carry implementation status and measured results.

The architecture references carry a Proposed banner until design approval. They become Current when Elsa switches to the new core; prototype publication alone does not change their status.

## Implementation and evidence

- [Prototype README](../../prototypes/kernel-model/README.md): components, runnable checks and commands.
- [Prototype design](../../prototypes/kernel-model/DESIGN.md): implemented contracts and invariants.
- [Findings](../../prototypes/kernel-model/FINDINGS.md): qualified results, limitations and counterexamples.
- [Write volume](../../prototypes/kernel-model/WRITES.md): PostgreSQL write and bloat measurements under the stated workload and settings.

Successful prototype checks do not approve a provider policy, a new default or product implementation. The program's design review and owner approval remain the gate before product work. At the switch, a shorter user-facing architecture section is published under `website/docs/`, linking to the engineering references here.
