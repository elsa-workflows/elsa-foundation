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
