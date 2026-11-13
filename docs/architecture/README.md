# Execution architecture

These pages describe the proposed clean-slate execution kernel. They are design references, not approval to replace Elsa's current runtime. [Program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) tracks the proposal, qualification and switch. The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md) records the owner's decisions and release scope.

## Reading order

1. [Kernel walkthrough](https://vowful-mustard-pd8t.here.now/): the layers, records, turns, ownership and execution models, with interactive diagrams. [HTML source](../reports/clean-slate-kernel/walkthrough.html).
2. [The kernel in motion](https://crystal-pellet-h5m3.here.now/): follow a workflow through persistence modes, faults and the fence lab. [HTML source](../reports/clean-slate-kernel/kernel-in-motion.html).
3. [Storage model](storage-model.md): the provider contract, record lifecycles, writers, invariants, query paths, indexes and retention costs.
4. [Ownership and fencing](ownership-and-fencing.md): why leases need a fence, how take and commit interact, and the costs and alternatives.

The walkthrough's data cards link to the storage reference. Its ownership chapter links to the fencing explanation. These references explain the contract; experiment reports carry implementation status and measured results.

## Implementation and evidence

- [Prototype README](../../prototypes/kernel-model/README.md): components, runnable checks and commands.
- [Prototype design](../../prototypes/kernel-model/DESIGN.md): implemented contracts and invariants.
- [Findings](../../prototypes/kernel-model/FINDINGS.md): qualified results, limitations and counterexamples.
- [Write volume](../../prototypes/kernel-model/WRITES.md): PostgreSQL write and bloat measurements under the stated workload and settings.

Successful prototype checks do not approve a provider policy, a new default or product implementation. The program's design review and owner approval remain the gate before product work. At the switch, a shorter user-facing architecture section is published under `website/docs/`, linking to the engineering references here.
