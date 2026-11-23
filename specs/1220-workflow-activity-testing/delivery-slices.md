# Delivery slices

Resume audit: 12 November 2026, against main `dff7c490d0e5501b22d4ba89beb389c956d4f423`.

## What landed

PR [#3903](https://github.com/elsa-workflows/elsa-foundation/pull/3903) closed without merge. Main contains neither `Essentials/src/Workflows/Testing` nor this specification. The shared saved regression-testing capability remains open under #3588/#3892.

Adjacent work did land and must be preserved during extraction:

- Scheduler drain state reuse: `3e4b7f2cb8` (#4054). Reinspect the current pipeline before adding a parallel header/context carry path.
- Activity-result member bindings: `04fcc3181e`, `0a4c2a7eb8`, `c970817ae9`, `893c97cef7` (#4053/#4078). Rebase target/setup and expression compatibility work on these contracts.
- Ownership-fenced checkpoint writes: `bde46c3f53`, `ae248a69a6` (#4089/#4166). Preserve those checks when extracting test-scope transactions.
- User Task authoring/result inspection: `90d253fd2b` (#3847). Reuse the current owner contracts when adding test actions.

These commits establish dependency drift, not testing-feature acceptance. The latest main CI run was queued at audit time, and #4177 remains open. No healthy-main or resulting-main qualification is asserted.

## Preserved work

Snapshot `ef20ce0e37` in the original a299 worktree preserves the local follow-up implementation on top of `38ee6328b7`. The closed foundation head is `73592f256e`. Keep both intact; extract selected files/hunks into branches based on current main instead of merging the whole snapshot.

Prior component counts are historical. Temporary evidence directories are unavailable after the pause; do not publish those counts as fresh gates. Regenerate targeted checks for each extracted head.

## Extraction order

The closure request on #3903 calls for stacked PRs of roughly 3,000 lines, one specification slice each. This table refines the existing task order without dropping any acceptance criterion. Measure actual diffs before opening each code slice; split again when needed.

| Order | Spec tasks | Reviewable result |
|---|---|---|
| 1 | T001–T005 | Restore the shared specification, contracts, acceptance and delivery order; no production activation. |
| 2 | T006–T009 | Bounded normal-runtime witnesses, then exact native entry/context contracts and behavior in separate slices. |
| 3 | T007–T010 | Lexical ownership/capture, native persistence fences and transport fences, separated by owning boundary with causal controls. |
| 4 | T011–T013, T022 | Reviewed Testing purpose admission, common case/report contracts, deterministic assertions and CI report policy. |
| 5 | T014–T015 | Typed saved revisions/capture, preparation responsibility, then final admission/results as separate transactions and PRs. |
| 6 | T016–T018 | Publishing candidate preparation, retention, target/rebind and setup compatibility, each independently qualified. |
| 7 | T019–T026 | Contributions/effect closure, ordered steps, settlement, lifecycle and fixtures; one owner boundary per slice. |
| 8 | T027–T033 | Authorized authoring API, portable codec/import/export, durable time and owner-qualified interactions/integration mappings. |
| 9 | T034–T051 | Studio manual-to-saved flow, suites/history, packaged CLI, normal-host activation and composed journeys after backend prerequisites. |
| 10 | T052–T059 | Documentation, complete acceptance, independent review, exact-head CI, merge and resulting-main closeout. |

Generated migration designers can exceed the target alone. Measure those separately before the provider slice and resolve review packaging explicitly; do not silently exempt generated files from the requested size limit.

No slice auto-closes either feature until its complete agreed acceptance is met. Program #3901 is not activated by this plan. The first implementation leaf follows the source witnesses; the preserved unfinished admission draft does not jump ahead of that order.
