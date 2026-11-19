# Execution architecture

> **Approved design, with conditions.** Gate 2 granted design approval on 18 November 2026 at
> `addffea7c850063d1ebe710b07866ff1640a93cb` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)). The implementation evidence is owed at K1 to K12,
> and the [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions) stay open until it lands.

These pages describe the clean-slate execution kernel's approved design. They are design references; replacing Elsa's current runtime is gate 4, the switch. [Program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) tracks the proposal, qualification and switch. The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md) records the owner's decisions and release scope.

## Reading order

1. [Kernel walkthrough](https://vowful-mustard-pd8t.here.now/): the layers, records, turns, ownership and execution models, with interactive diagrams. [HTML source](../reports/clean-slate-kernel/walkthrough.html).
2. [The kernel in motion](https://crystal-pellet-h5m3.here.now/): follow a workflow through persistence modes, faults and the fence lab. [HTML source](../reports/clean-slate-kernel/kernel-in-motion.html).
3. [Storage model](storage-model.md) — **Approved design**: the provider contract, record lifecycles, writers, invariants, query paths, indexes and retention costs.
4. [Ownership and fencing](ownership-and-fencing.md) — **Approved design**: why leases need a fence, how take and commit interact, and the costs and alternatives.
5. [Fairness and backpressure](fairness-and-backpressure.md) — **Approved design**: admission at the edge, per-tenant limits, fair turn selection within a pass, and the bound on inbox growth.
6. [Large fan-out and fan-in](large-fan-out.md) — **Approved design**: the combiner tree with credits, its costs against a single parent, and release 1's cap on a parallel ForEach.
7. [Data lifecycle](data-lifecycle.md) — **Approved design**: where payloads live, retention and deletion of finished instances, erasure, encryption at rest, and what the journal format must fix now.
8. [Write volume and bloat](write-volume.md) — **Approved design**: bytes per commit and dead tuples before and after the mitigations, values by reference as release 1's priority, and the lazy-snapshot fallback.
9. [Migrating running instances](migrating-running-instances.md) — **Approved design**: state versions per model and per activity, the migration command and each container's hook, what in-flight work does, and release 1's simple cases.
10. [Job workers outside the process](job-workers.md) — **Approved design**: one job contract for in-process and remote workers, attempt leases, heartbeats, the effect key, completion through the fenced commit, and the release placement of standalone Jobs.
11. [Observability and operator actions](observability.md) — **Approved design**: trace context on every message, stable metric names, inspect, retry and discard of parked work, and the read models operators use.
12. [Tenancy](tenancy.md) — **Approved design**: where the kernel takes the tenant from, tenant-qualified names, and the uniqueness and authorization rule of every ingress, match, read and administrative operation.
13. [Decision register](decisions.md) — **Approved design**: every owner decision that binds the kernel, by topic, with its date and source.

The walkthrough's data cards link to the storage reference. Its ownership chapter links to the fencing explanation. These references explain the contract; experiment reports carry implementation status and measured results.

The architecture references carried a Proposed banner until design approval, and carry the Approved design banner from gate 2's approval on 18 November 2026. They become Current when Elsa switches to the new core; prototype publication alone does not change their status.

## Implementation and evidence

- [Prototype README](../../prototypes/kernel-model/README.md): components, runnable checks and commands.
- [Prototype design](../../prototypes/kernel-model/DESIGN.md): implemented contracts and invariants.
- [Findings](../../prototypes/kernel-model/FINDINGS.md): qualified results, limitations and counterexamples.
- [Write volume](../../prototypes/kernel-model/WRITES.md): PostgreSQL write and bloat measurements under the stated workload and settings.

Successful prototype checks do not approve a provider policy or a new default. Implementation starts at K1, the prototype Worker refactor, before product wiring. Product slices follow from K2, behind a host switch, with the old core the default until gate 4. The work follows the [program plan](../program-goals/clean-slate-kernel.md#milestones) milestone by milestone, and each milestone closes on its own evidence. At the switch, a shorter user-facing architecture section is published under `website/docs/`, linking to the engineering references here.
