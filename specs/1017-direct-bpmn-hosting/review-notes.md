# Ordered component reviews

These are pre-implementation component reviews. They do not count as complete-PR review rounds or establish runtime acceptance.

## Specification

Base: `d3ebb980e2dbebeed680808e58c1edd694fae0f7`.

| Iteration | Exact reviewed head | Correctness | Standards | Acceptance |
|---|---|---|---|---|
| 1 | `ad683917c41d46f079bd3d7af7fafc2104a456c3` | Narrow stale-owner guarantee to new decisions/owed-work acceptance; committed physical delivery can finish | Clean | Require actual pipeline cancellation and remaining-work disposition, including already-terminal truth |
| 2 | `10af302d89ba10d3c91d8064d6c3c14b1123cf9c` | Clean | Clean | Clean |

Both findings were fixed by `10af302d8`. [Public specification-review receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6461747498).

The three reviewers inspected independently without seeing each other's outputs. No implementation or behavioral tests existed at this gate. The author checked requirement numbering, mandatory sections, absence of template markers, feature selection and relative evidence links. Specification coverage is not a runtime result.

## Plan

Iteration 1 at `7a3d36adbcebf063a767bfab48a1fd55688d03de`: standards clean; correctness required an authoritative lifecycle-read/terminal-settlement policy before native cleanup reuse; acceptance required a nonterminal retryable-incident lifecycle and actual retry proof without reviving final semantics. Both were fixed by `66e691224`. Author source review also found Initial-regeneration incompatible with retained baseline databases; that commit requires a narrow incremental Runtime migration mode. Iteration 2 at `66e69122411e1aeb3ff78ba20dbbe8c9c94abf03` is clean across all three independent lenses. This qualifies the plan for task breakdown, not runtime acceptance.

## Tasks

Iteration 1 at `62c33169a19c71fb57a352709a0ab36e27334f6b`: acceptance clean; correctness and standards identified the same missing T025 bridge dependency. Fixed by `647f7f54f`: T025 waits for T027/T034–T035 and T040 completes its real settlement arms. Iteration 2 at `647f7f54fc797a3ef205385e72f7ccf0373bcfa8` is clean across all three independent lenses. Read-only cross-artifact analysis at that same head maps all 22 FRs/six SCs to the 47 tasks with no unresolved findings. This permits T001 capture tooling, followed by actual T002 capture before affected writers; it is not runtime acceptance.

## Complete PR

Authoritative complete-PR round receipts and merge audit live in the [PR3410 conversation](https://github.com/elsa-workflows/elsa-foundation/pull/3410#discussion_bucket). This file indexes that record instead of maintaining a second round counter. The loop covers the full implementation diff and all issue/PR discussion, uses three independent lenses, and has a six-round bound. Required exact-head checks, human holds, pending owner decisions and protected merge rules apply.

The [FR/SC acceptance index](evidence/qualification.md) and [affected-main runtime/legacy/Studio repeat](evidence/main-adoption/d7ee22b16/README.md) are qualification inputs, not a clean review round or merge audit. Round1 reopened the spec to In progress for recovery/standards repairs; Implemented status and checklist retirement are restored only after those corrections qualify, and become shipped at merge. No broader program completion is inferred.

Round1 repairs and the reopened affected qualification now have [source-bound runtime/legacy/API receipts](evidence/reviews/round-1/repair-qualification/README.md), [five actual inverse-cancellation journeys](evidence/pipeline/inverse-cancellation-order/README.md) and [final affected Studio proof](evidence/reviews/round-1/studio-qualification/README.md). Full-PR round2 remains due; this index does not create another round.

The adopted-template [repair](evidence/main-adoption/931123c7e/repair/README.md), [Studio qualification](evidence/main-adoption/931123c7e/studio-qualification/README.md) and [updated API audit](evidence/main-adoption/931123c7e/api-reconciliation/README.md) supersede only the need to repeat those affected journeys; older receipts retain their sources. The two missing shared EF inventory entries are corrected with594/594 local architecture tests; exact final required CI is still due. This prepares runtime acceptance/spec lifecycle for review, retaining owner magnitude and deferred scan holds. Full-PR round2 has not begun at this documentation commit; the PR conversation remains the sole round counter.

[Round2 reports/diagnostics](evidence/reviews/round-2/README.md) retain six surviving findings. The authoritative completed-round receipt remains the PR conversation. Spec is In progress pending corrections/qualification; earlier positive source-bound journeys remain valid within their scope.

[Round2 correction qualification](evidence/reviews/round-2/qualification/README.md) passes six changed whole projects6165/6165 and four actual first-terminal native-parent journeys at executinge3ec219fc. All six findings have corrections, red-to-green behavior and scoped compatibility reconciliation. [Completed round2 receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6526582380) remains authoritative; full-PR round3 is next, not yet a clean-loop claim. Runtime qualification restores Implemented preparation; owner/scan/required-CI/protected/post-main gates remain open.

[Round3 reports, five findings and actual pre-fix checkpoint](evidence/reviews/round-3/README.md) reopen affected acceptance. The public completed-round receipt remains authoritative; fixes and exact-head round4 are due, with three rounds left.

Round3 corrections now include [actual old-fault standard-host recovery](evidence/reviews/round-3/fault-correction/host-qualification/README.md) and [all four actual embedded ETL-origin cancellations](evidence/pipeline/embedded-pipeline-cancellation/README.md). All five findings have evidence; the PR conversation remains authoritative for the completed-round count.

[Final affected round3 qualification](evidence/reviews/round-3/qualification/README.md) passes all6189 cases across six changed projects with zero skips, and repeats both actual saved Studio families on source76e8139dc: four completed new runs,12 whole stores,13 passing controls and unchanged original authoring/state. The original31/39 source-authority setup failure remains retained; normal captured SourceId configuration yields39/39 without changing production authority. T041/T043/T044 and Implemented preparation are restored; shipped status begins at merge. Exact-head round4 is next, without resetting the existing six-round bound or clearing owner/scan/CI/protected/post-main holds.

[Round4 reports and pre-fix captures](evidence/reviews/round-4/README.md) retain three P2 findings at exactcdc02819f: admission retention lease sharing, owned timer JSON context and an actual old admitted waiting worker with no automatic cleanup trigger. All are adopted, none dropped; this round is not clean. The actual old store and separate race/control stores were captured through coherent provider backups before fixes. Four full rounds completed, two remain. Spec/T041/T043/T044 reopen; owner/scan/required-CI/protected/post-main gates remain unchanged. The PR conversation remains the authoritative counter.

All three round4 findings have corrections: C1/S1 inee345b602 and A1 in178154c133. The [actual old fault recovery](evidence/reviews/round-4/fault-recovery/README.md) and [affected qualification](evidence/reviews/round-4/qualification/README.md) are inputs to the next complete-PR review, not another round. Current main4ab657f72 is integrated at30dfb0a9b. Current Studio/13 controls and10 public binary API comparisons pass; the last current-main host matrix passes40/40, zero skips. A subsequently observed frontend-only main change is being integrated with an affected editor smoke check. [Current public checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6534696556). The PR conversation remains authoritative; no clean round or delivery clearance is inferred.
