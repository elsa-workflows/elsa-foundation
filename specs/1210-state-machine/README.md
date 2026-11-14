# Elsa 4 StateMachine: requirements and current status

**Status, 14 November 2026:** active; M0 remains incomplete. This directory records the product requirements,
historical design research and the remaining delivery order. It does not enable a production StateMachine activity.

The [specification](spec.md) preserves the 49 accepted decisions in
[program #3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815).
The [acceptance matrix](acceptance-matrix.md) maps those decisions to observable scenarios.
Approvals, revisions and withdrawal remain sample application concepts, not Elsa domain types.

## What has landed

| Work | Verified disposition |
|---|---|
| Program establishment, #3827 | Merged as `7b87865750`; the program exists on main. |
| Preserve superseded attempts during cancellation, #3886 | Merged as `b3653923e9`; this is a generic cancellation fix, not StateMachine delivery. |
| StateMachine specification and runtime research, #3840 | Closed **unmerged** on 9 November at `9eaed8b330`; resubmit in reviewable slices. |
| Shared Message and standalone-trigger drafts, #3904 and #3912 | Both closed **unmerged** on 9 November. Their candidate contracts are not installed dependencies. |
| Creation identity, [#4451](https://github.com/elsa-workflows/elsa-foundation/pull/4451) | Merged as `b82a8b75a6`; private exact-creation/addressing prerequisite, not provider/product rollout. |
| Concurrent native phases, [#4479](https://github.com/elsa-workflows/elsa-foundation/pull/4479) | Merged as `45a7b6cde1`; ordinary regional work and hierarchy/phase barriers, not normal incident/retry. |
| General structural selector, [#4485](https://github.com/elsa-workflows/elsa-foundation/pull/4485) | Merged as `5e11e96681`; one compiled engine, with explicitly proposed ancestor behavior; not full SCXML compliance. |
| Full-graph native bridge, [#4487](https://github.com/elsa-workflows/elsa-foundation/pull/4487) | Merged as `d9fb8edc5d`; normal private hydration, durable current/planned visits and fixed phase work. |
| Generic native lexical resources, [#4490](https://github.com/elsa-workflows/elsa-foundation/pull/4490) | Merged as `995a5e76ae`; values stay with the normal native owner. |
| StateMachine visit scopes, [#4495](https://github.com/elsa-workflows/elsa-foundation/pull/4495) and [#4505](https://github.com/elsa-workflows/elsa-foundation/pull/4505) | Merged as `383c9eb9f8` and `db416ee7c8`; private literal defaults, exact visit ownership, source Action lifetime and fresh/retained scopes across durable phases. |
| StateMachine runtime and Studio | The earlier inventory found no production activity/designer at `dff7c490d0`. The private prototypes above do not deliver M1–M3. |

The latest visit-scope result's entire main tree matches its reviewed proof source. Its 55 cases are separately
labelled as 24 actual workflows, 17 literal fixtures and 14 seeded context refusals; the runner
[documents commands and limits](../../prototypes/kernel-model/models/Statecharts.Run/README.md).
The [values proposal](contracts/values.md) keeps Action-after-Exit visibility explicit as engineering direction.
Passing targeted prototypes does not establish full main CI health; [#4177](https://github.com/elsa-workflows/elsa-foundation/issues/4177)
remains separately tracked. Exact review/run/merge receipts stay on the linked PRs and program issue.

The closure instruction is on [#3840](https://github.com/elsa-workflows/elsa-foundation/pull/3840#issuecomment-6739908133).
The archived branch and its evidence remain useful source material. Closure is neither rejection of the feature
nor evidence that its implementation landed.

## Architecture reconciliation

The [12 November clean-slate decision](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6817582832)
supersedes the earlier journal-core proposal. The subsequent
[layering and persistence decisions](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6823051291)
separate actor placement, durable actors and execution models. These are kernel-program decisions;
StateMachine does not approve its product design or bypass its approval gate.

The `m0-*.md` files and `research.md` below preserve the earlier native-composite investigation. Their C2 mapping,
per-entity projections, candidate schema successors and command-number proposals are **historical mechanisms to
reassess**, not requirements to port to the new kernel. Keep their underlying product obligations: exact invocation
identity, durable admission independent of authored work, bounded receipts, causal trigger groups, state-visit
lifetimes and recovery that preserves successful work. Select the smallest mechanism that proves those obligations
on the agreed runtime. Do not add another scheduler, publication lifecycle or source of execution authority.

Root and nested use, ordinary activity composition, hierarchy and parallel regions remain first-release scope.
The new architecture does not implicitly select a separately authored StateMachine execution model or narrow scope.

## Evidence and remaining work

Historical test and witness figures in the research describe their named commits and configurations. They are not
runs of current main, current PR checks, production StateMachine acceptance or approval of Q1–Q3. Links to witness
code and original evidence point to the immutable archived commit so this specification slice does not bundle the
large research executable or raw result files.

Follow [the delivery order](delivery.md). M0 must resolve complete statechart selection/recovery, durable input
integration, the Studio contract and the qualified native-compensation topology. Only then can the applicable
production implementation slices claim that contract. Current-head tests, real durable and Studio journeys,
independent acceptance, protected merge and resulting-main verification remain required.

The [selection and phase contract](contracts/selection.md) remains an engineering proposal. The compiled engine
and native phases above qualify a bounded subset. Ordinary trigger/rearming, automatic/completion arbitration, loop
recovery and complete source grouping remain open; neither the engine nor its phase proof closes Q1.

The bounded [selection reference model](selection-model/README.md) makes candidate and phase-plan rules
executable. Its tests are algorithm evidence only; actual runtime integration and the remaining Q1 rules stay open.

The [Studio authoring and inspection proposal](contracts/studio.md) defines Q3's document, navigation,
validation and operator journeys before implementation. Its acceptance journeys are planned, not observed UI proof.

The proposed [source-wave work unit](source-wave-plan.md) addresses shared-publication grouping and native
readiness before chart selection. Source installation, transitive responsibility, catch-up/call boundaries
and recovery remain planned qualification. The identity prerequisite has landed; applicable shared source-owner
review before executable adoption and actual product architecture agreement remain separate gates.

The proposed [ordinary recovery profile](contracts/recovery.md) maps existing intervention/successor semantics before
new native implementation: fresh activity identity, pinned inputs/operation/provenance, atomic parent rebinding and
completed-work preservation. Its initial leaf/known-parent profile is deliberately bounded; ordinary root and other
activation boundaries, actual chart/admission integration and complete operator recovery remain required. It is a
technical proposal with no executable recovery, source-owner or product agreement implied.
