# Reviewable delivery slices

Status: delivery plan, not implementation acceptance. Parent: [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590).

The owner closed [#3911](https://github.com/elsa-workflows/elsa-foundation/pull/3911) without merge on 9 November:
the feature was too large to review as one unit. Resubmit stacked PRs of roughly 3,000 changed lines each,
one spec slice per PR in the [task order](tasks.md). The owner resumed the remaining work on 12 November.
This plan preserves the original native, BPMN and ETL scope and all success criteria.

## Baseline and evidence

Main inspected at `dff7c490d0e5501b22d4ba89beb389c956d4f423` contains the active authorization in ADR 0080
and the program registry. None of #3911's release packages, spec or dedicated goal page is on main.
There is no competing open release-promotion PR at resumption. The old branch and its uncommitted owner work
are retained separately; they are inputs for review, never copied wholesale over current main.

The unpublished minimal scheduler-claim fix passed four targeted cases on its named candidate. It belongs
with the deployment-claim integration slice; it is not a standalone main regression because the new claim path
has not landed. See [historical evidence](historical-evidence.md). Three other failed cases and seven unrun
commands remain historical gaps. Refresh them when their owning source is brought forward.

The open [main-CI incident #4177](https://github.com/elsa-workflows/elsa-foundation/issues/4177) is tracked separately.
Do not infer a healthy base from this source inspection, or bypass a red required gate when merging.

13 November refresh: current base is `0903e51653`; the original baseline above remains historical.
Spec 1225 replaces the initial resubmission's 1224 after #4225 brought another 1224 spec onto main.
Every later slice restates its own exact base; the program choices and acceptance remain unchanged.

## Dependency order

These are review boundaries, not new issues or a waiver of the complete-delivery criteria. Estimated sizes
include tests and public API tracking; split a row further when needed to stay reviewable. Each production PR
names its exact task subset, parent, current source, validation and remaining acceptance. A partial row never
checks off an entire task.

| Slice | Task subset | Concrete deliverable | Required proof before acceptance |
|---|---|---|---|
| 1. Contracts and delivery plan | T001–T004 refresh | Restore reviewed product choices, contracts, task list and goal page; reserve unique spec 1225; distinguish historical implementation from main | Relative links, requirements consistency and independent design/standards review; no runtime claim |
| 2. Immutable transport | T005–T008 transport subset | Runtime-safe bundle contracts, bounded canonical codec, exact blob preservation, optional composition and packed consumer | Whole transport project, malicious/oversized input controls, meaningful digest mutation, clean packed consumer, structural registration |
| 3. Owner semantic verification | T009/T013 shared prerequisite | Compare owner-discovered facts, complete edges, selected root roles and scoped requirements with the frozen manifest | Whole semantic suite and root/edge/fact mutations; no permissive default owner |
| 4. Archive | T009 durable-storage prerequisite | Scoped immutable archive contracts and SQLite implementation with consumer-owned context | Raw byte integrity/bounds, atomic retry, pooled concurrent readers/writers, killed writer/reopen; split contracts/store and provider tests if necessary |
| 5. Runtime codecs | T010/T013 verification prerequisite | Runtime-owned template hashing and bounded exact native/reusable inspection | Existing hash goldens, full-pin/tamper controls, Runtime-only packed dependency closure |
| 6. Resource identity | T017 prerequisite | Connection incarnation/revision observation and same-read/save enforcement | Rotation, stale save, delete/recreate and supported real-provider cases; no retained mapping claim yet |
| 7. Retained source framework | T009 capture subset | Source callbacks/contributors held through complete verified durable capture | Whole capture suite, cancellation/unknown commit/bounded work and actual archive acceptance |
| 8. Native source and import | T010/T013/T015 native subset | Exact authored-version associations, executable/reusable material and owner-discovered closure | Real publication/capture/import, producer-versus-selected-version distinction, source GC and conflicts |
| 9. BPMN source and import | T011/T013/T015 BPMN subset | Exact direct/ordinary BPMN model/worker closure and physical-locator association | Actual owner round-trip, no byte rewriting, dependency-only fresh-start refusal; split by owner seam |
| 10. ETL source and import | T012/T013/T015 ETL subset | Exact retained v1/v2 integer-version import and read-only owner catalog | Arbitrary retained version, original bytes, conflict/corruption and runtime consumption; split codec/source/import as needed |
| 11. Creation and inspection | T014 and creation portion of T021 | Source selection/download and equivalent release archive API/CLI inspection | Actual source host and installed packaged CLI; include Studio creation/reopen acceptance |
| 12. Target context and preview | T016–T021 remaining | Stable target identity, trusted scope, immutable mapping capture, fresh preview/apply admission and prepared target sessions | Two targets with different mappings, denied/tenant/incarnation/stale controls and equivalent Studio/API/CLI previews |
| 13. Owner activation and propagation | T022–T025 | Atomic owner guard/effect/receipt, publication-slot model admission and per-run context across all native/BPMN/ETL boundaries | Whole owner suites, first claim/deferral/reopen/competing workers, stale fencing, real retained-run and delayed-admission witnesses; separate bounded owner/context PRs |
| 14. Coordination and rollback | T026–T029 | Durable fixed-plan journal, staged dependency roots, authenticated forward recovery and explicit rollback | Real interruption/unknown commit/overlap/supersession/GC, visible mixed interval and pinned old/new runs on both targets |
| 15. Complete user journeys and closeout | T030–T037 | Full Studio/pipeline operations, history/evidence, operator docs, examples and all agreed acceptance | Real packaged CLI/composed Studio, inspected downloadable evidence, independent UX review, exact-head required gates and resulting-main qualification |

Slice 8 is further divided at owner boundaries: (8a) finite consistent Runtime source reads, (8b) bounded
retention and Runtime dependency acquisition, (8c) exact native/reusable catalog snapshots and source bridge,
then (8d) owner import/staging and actual round-trip witnesses. Each remains a partial T010/T013/T015 subset;
read contracts or controlled framework tests do not establish complete native capture/import acceptance.

Only one implementation slice is active. A stack may carry reviewed prerequisites, but each child is reviewed
against its named parent and states the prerequisite's merge/qualification status. Recheck main and competing
PRs before committing. Do not resurrect the closed monolithic PR or reuse its branch for the new stack.

New contracts alone do not complete M0: concrete owner witnesses and current review remain required by
[the plan](plan.md). No stage alone closes #3590. Production/shared deployment, package publication, schema/server
upgrade, business-effect reversal and migration of running instances remain outside scope.
