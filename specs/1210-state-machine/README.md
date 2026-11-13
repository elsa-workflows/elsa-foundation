# Elsa 4 StateMachine: requirements and current status

**Status, 12 November 2026:** resumed; M0 remains incomplete. This directory records the product requirements,
historical design research and the remaining delivery order. It does not enable a StateMachine activity.

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
| StateMachine runtime and Studio | No production StateMachine activity or designer was found on main `dff7c490d0`. M1–M3 remain open. |

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

The [selection and phase contract](contracts/selection.md) is the next engineering proposal. It separates chart
rules from storage/actor placement and labels its new choices and remaining qualification; it does not close Q1.

The bounded [selection reference model](selection-model/README.md) makes candidate and phase-plan rules
executable. Its tests are algorithm evidence only; actual runtime integration and the remaining Q1 rules stay open.

The [Studio authoring and inspection proposal](contracts/studio.md) defines Q3's document, navigation,
validation and operator journeys before implementation. Its acceptance journeys are planned, not observed UI proof.
