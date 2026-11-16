# Studio UX acceptance before delivery

**Status**: Implemented
**Issue**: #3776
**Program**: Elsa 4 Simplification #2559

## Problem and outcome
Passing isolated tests has repeatedly left the owner to discover obvious layout and interaction defects. Studio delivery must carry evidence of the composed user experience, and independent review must evaluate that evidence before declaring completion.

## Acceptance
1. Before implementation, record the user goal, an existing reference screen, observable acceptance states and risk-based coverage in the issue or spec. Workflow Definitions and the Connections collection are initial visual references.
2. Relevant PRs run a small real Studio/Workbench source acceptance suite, with normal modules and authentication. Owned temporary stores/processes prevent modification of existing developer hosts. No intercepted response or component fixture counts as composed proof.
3. The initial suite exercises collection empty/populated/search recovery and connection creation, plus Agent create/editor/navigation. It captures light/dark and desktop/narrow states and checks relevant keyboard/focus/overflow behavior. It does not call external model providers.
4. Every run retains screenshots, accessibility snapshots, browser traces, failures and source identity. A missing environment, zero tests, skipped test, failed test, missing artifact or timeout fails; successful assertions are not visual approval.
5. Existing required Build & test consumes selected Studio frontend/browser/.NET and composed UX results. Unrelated PRs preserve fast paths. Tests must demonstrate failures cannot disappear through a skip, missing output or docs-only bypass.
6. Independent UX acceptance review inspects screenshots and the whole affected journey against the reference, including states introduced by the feature. Its receipt names exact head, source run/artifacts, findings, retest and limits. The author fixes and re-establishes evidence; stale or missing proof means incomplete acceptance.
7. Extend shared components and focused regressions when defects recur. Coverage follows interaction risk; no automatic full Cartesian matrix, screenshot baseline blessing or indiscriminate tests.

## Boundaries
No new model API/credential, schedule, public service, architecture or administrator ruleset change. Reuse the existing independent review loop and required aggregate; do not pretend a metadata validator can certify visual judgment. Initial qualification can name unmerged prerequisite heads explicitly. Any unavailable real-host/independent evidence is reported as incomplete, never passed.

## Rollout evidence

Prerequisites #3769 and #3774 landed at `d88e0cdd557` and `2e2203504a8`. The main-based clean source `016b70ef08f289f956b9fad1a2e705dd8dd1612e`, based on main `e6550a960`, passed all eight composed cases and independent inspection of desktop/narrow and light/dark captures. The implementation is complete; hosted exact-head artifact review and resulting-main activation remain delivery gates tracked on #3776 / PR #3782, not claims established by that local result.
