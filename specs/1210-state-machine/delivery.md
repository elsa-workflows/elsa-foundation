# StateMachine delivery after the 9 November triage

**Status:** proposed work order, resumed 12 November. This replaces publication of the large PR #3840;
it does not approve the historical runtime mapping or declare M0 complete.

The [product specification](spec.md) and [owner-decision matrix](acceptance-matrix.md) retain the complete first release.
The [current status](README.md) distinguishes landed changes, closed drafts and historical research.
Use existing program #3815 and its children; this document creates no new program or issue hierarchy.

## Ordered slices

Keep each PR near the requested 3,000 changed lines or smaller. A row may require several PRs; split by a coherent
contract or behavior and retain the evidence on that PR. Do not mix unrelated stages to fill a size allowance.

| Order | Existing owner | Reviewable outcome | Required boundary before dependent work |
|---|---|---|---|
| 1 | M0, #3820 | Recover accepted requirements, acceptance scenarios and clearly labelled historical proposals from #3840. Record current-main and kernel reconciliation. | Review confirms all 49 decisions remain traceable and no unmerged mechanism is represented as landed or agreed. |
| 2 | M0, #3820 | Complete Q1's selection and phase contract, including hierarchy fallback, regional conflicts, visit boundaries, automatic/completion ordering, trigger rearming and loop recovery. | Deterministic worked traces and meaningful negative cases cover each open rule. Preserve accepted semantics; separate any proposed engineering choice from owner decisions. |
| 3 | M0, #3820 | Reconcile Q2 with the agreed runtime: exact addressed admission, capacity, retained duplicate/conflict receipts, causal source groups, ordered evaluation, cancellation and uncertain commits. | A bounded integration witness proves the actual commit/receipt/control boundary, ordinary waiting activity work and restart. An inbox enqueue or a pure reducer is not admission proof. |
| 4 | M0, #3820; Studio, #3824 | Define Q3's chart, hierarchy outline, activity drill-in, transition inspector, validation and execution inspection. Settle the saga-style example's topology and journal lifetime. | Root/nested author and operator journeys, keyboard/read-only/narrow-width states and exact-version evidence have explicit acceptance criteria. Obtain applicable design agreement before production implementation. |
| 5 | M1, #3821 and #3822 | Implement generic root/nested execution and durable hierarchy using ordinary Elsa activities, normal values, outcomes and publication. Deliver the associated Studio authoring/inspection slice. | Actual save/reload/publish/run, parent fallback, fresh deadlines, stale notifications, durable phase waits, cancellation and recovery pass on the supported runtime. |
| 6 | M2, #3823 | Add parallel regions and their Studio surfaces. | One input drives compatible transitions; explicit order resolves conflicts; phase barriers, all-region finality and successful sibling work survive waits, faults and restart. |
| 7 | M3, #3824 and #3825 | Complete accessible authoring/inspection, public integration and documentation; qualify native-compensation composition and the adverse-path matrix. | Current-source root/nested Studio-to-runtime journeys, providers and normal permissions/reuse/versioning, negative controls, required checks, independent acceptance, protected merge and resulting-main verification. |

## Immediate M0 work

1. Inventory the historical Q1–Q3 proposals against the current kernel decision and its exact source. Keep the
   4–6 November runtime APIs as reference evidence; avoid building new current-core persistence adapters solely
   because those adapters already exist on the unpublished branch.
2. Specify the statechart algorithm independently of storage and actor placement. Use the accepted document
   review example for traces, keeping all types and engine rules generic. Record intermediate selected phases and
   visit identities so waiting/failure behavior can be assessed before integration.
3. Prove how that algorithm composes with ordinary activity work on the selected runtime. Compare a native
   composite and an execution-model integration only against root/nested composition, normal lexical values,
   publication, durable receipts and native compensation. A prototype's existence does not settle placement.
4. Update the technical plan and dependency-ordered implementation tasks from the observed result. Identify the
   smallest remaining shared runtime change with its actual consumers; do not inherit the historical command
   allocations, provider-capability flags or schema successors as already accepted contracts.

## Validation and delivery rules

The first slice is documentation only: validate local links and decision/requirement references, and run the
normal documentation checks. It claims no new .NET, provider, host or Studio results.

For behavior changes, test the changed behavior and the affected projects through their normal gate. Reconcile
discovered and executed tests, preserve failures and skipped/unrun stages, and distinguish prototype/witness
results from production acceptance. UI changes require inspected evidence from the composed host. Existing
execution, value, authorization, publication, retention and recovery owners remain authoritative until their
own approved replacement lands.

Each slice states its exact source and what remains. No intermediate PR closes #3820 or #3815 merely because
its own small scope passed. No slice revives the old Azure lease or grants infrastructure expansion.
