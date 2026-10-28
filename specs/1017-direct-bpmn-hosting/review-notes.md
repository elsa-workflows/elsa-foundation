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

Pending. No source implementation is authorized by a clean specification review alone; the plan and tasks must be reviewed in order, followed by actual pre-writer captures.

## Tasks

Pending.

## Complete PR

Zero rounds. Final review must cover the full implementation diff and all issue/PR discussion, use the three independent lenses, and stay within the six-round bound. Required exact-head checks, human holds and protected merge rules apply.
