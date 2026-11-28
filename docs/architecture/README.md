# Execution architecture

The published Curated Essentials server defaults to the built-in Actor Foundation for Native and
Flowchart workflows on SQLite and PostgreSQL. The default switch (K13) was published in
`4.0.0-preview.3013`. The shared-format Curated profile was subsequently published in
`4.0.0-preview.3019`. Earlier-runtime removal (K14) is in progress. See the
[K13 receipt](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7231238178),
[shared-format receipt](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7231935018)
and [program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229).

Start with the [architecture tour](../architecture-tour.md) and
[how a workflow executes](../how-a-workflow-executes.md) for the shipped path. Deployment settings
and data custody are in the [Curated Essentials profile guide](../../docker/profiles/essentials-curated/README.md#workflow-execution).
The [pinned earlier Full deployment record](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7232199720)
covers compatibility workloads still using the earlier runtime.

The references below preserve the design decisions and prototype work, including values by
reference, remote job workers, StateMachine and Park operations beyond recovery. Each page keeps
its design status. Consult the [owner's trimmed release scope](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7142657677)
for which work is part of this delivery. The [architecture verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)
records design approval with conditions at `addffea7c850063d1ebe710b07866ff1640a93cb`.

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
13. [The foundation contract](foundation-contract.md) — **Approved design, with conditions** (19 November, at `2825b0ed1`): the contract between a foundation (what layers 1 and 2 provide) and layer 3, in plain domain words; its vocabulary, the built-in foundation's mapping, the leak inventory and conformance cases FC-1 to FC-18.
14. [StateMachine on the kernel](statemachine-on-the-kernel.md) — **Owner decisions** (19 November): #3815's decisions OD1 to OD49 on the kernel, the four owner answers, and K7's invariants S1 to S4.
15. [Designer definitions on the kernel](definitions-on-the-kernel.md) — **Owner decisions** (25 November): K10's definitions-to-kernel path; a kernel activation coordinator, the executable mapper and host-evaluated JavaScript and Liquid bindings.
16. [Decision register](decisions.md) — **Approved design**: every owner decision that binds the kernel, by topic, with its date and source.

The walkthrough's data cards link to the storage reference. Its ownership chapter links to the fencing explanation. These references explain the contract; experiment reports carry implementation status and measured results.

## Implementation and evidence

- [Installed Actor Foundation packages](../../essentials/src/ActorFoundation/README.md) and [workflow hosting composition](../../essentials/src/Workflows/Runtime/KernelHosting/).
- [Retained Curated production journeys on both providers](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7231333589).
- [K14 retirement inventory](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7231865702) and [first production removal](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7232157480).

The following reports record prototype scope, settings and measurements.

- [Prototype README](../../prototypes/kernel-model/README.md): components, runnable checks and commands.
- [Prototype design](../../prototypes/kernel-model/DESIGN.md): implemented contracts and invariants.
- [Findings](../../prototypes/kernel-model/FINDINGS.md): qualified results, limitations and counterexamples.
- [Write volume](../../prototypes/kernel-model/WRITES.md): PostgreSQL write and bloat measurements under the stated workload and settings.

The [program plan](../program-goals/clean-slate-kernel.md#milestones) records the milestones and
their acceptance criteria. The [Essentials workflow execution guide](../../website/docs/essentials/workflow-execution.md)
covers the release from a user's perspective.
