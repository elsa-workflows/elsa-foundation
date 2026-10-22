# Tasks: Honest Secrets inventory coverage

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data model](data-model.md), [UI contract](contracts/inventory-ui.md), [validation guide](quickstart.md).

One issue/PR:3069. Brownfield observable regression cadence; one isolated implementation writer, root integration/review/QA. No API/runtime/value-disclosure change.

## Phase1 — Setup

- [x] T001 Refresh qualified base/prerequisite3062, winning claim6331801398, competing work and global spec allocation for specs/1002-studio-secrets-inventory/; retain3063/3067 branches and their separate gate state.
- [x] T002 Review current SecretsPage.tsx, secretsApi.ts/secretTypes.ts, supported backend query/metadata boundary and root spec/plan; include the independent external plan acceptance review. Existing Secrets normal composition needs no optional module setup.

## Phase2 — Foundational observation ownership

- [ ] T003 Extend shared response/record fixtures in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretsPage.test.tsx so loaded items and authoritative server total can differ; retain actual existing-UI assertion failures for partial/facet-empty coverage before implementing new copy.
- [ ] T004 Bind accepted result coverage to its submitted query and latest request/context cleanup in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx; add observable superseded-request/draft-versus-submitted cases in the existing SecretsPage tests. Do not change unrelated mutation semantics or global authorization.

## Phase3 — US1: Understand inventory coverage

Independent test:100loaded/101total with local facet subset/zero differs from complete100/1/0 and request failure.

- [ ] T005 [US1] Render honest displayed/local, loaded and completed-server-search counts with a concise supported beyond-cap direction in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx; preserve current facets and loaded-scope help.
- [ ] T006 [US1] Qualify local-facet empty copy and distinguish initial empty/no matches/loading/failure/partial states in SecretsPage.tsx and Client/src/__tests__/secretsPage.test.tsx. Base partial status on actual total versus loaded count, not request-limit equality.
- [ ] T007 [US1] Verify descriptor suggestions never become a complete inventory facet and unsubmitted search text cannot relabel accepted counts in Client/src/__tests__/secretsPage.test.tsx.

## Phase4 — US2: Find a later record

Independent test:known exact metadata record absent from the initial100 is returned through supported server search.

- [ ] T008 [US2] Extend Client/src/__tests__/secretsPage.test.tsx using the actual Search/Enter controls and request query to retrieve an exact named record beyond the first loaded result; preserve metadata-only response/presentation and truthful new-result count.
- [ ] T009 [US2] Extend Studio/tests/browser/secretScopesFixture.tsx and secret-scopes.spec.ts with a representative100-loaded/101-total scenario and search to the exact later record. Identify this as mocked browser evidence; root actual-endpoint proof remains mandatory.

## Phase5 — US3: Retain detail context

Independent test:search/filter→exact detail→Close/Escape retains applied criteria, corresponding results and focus.

- [ ] T010 [US3] Add/extend retained search/filter/detail-close and keyboard-return assertions in Client/src/__tests__/secretsPage.test.tsx; preserve existing SecretDetail dialog behavior.
- [ ] T011 [US3] Complete the browser journey in Studio/tests/browser/secret-scopes.spec.ts for detail close, both themes/narrow layouts and new guidance readability; use existing semantic tokens and modify module CSS only if concrete layout evidence requires it.

## Phase6 — Polish and handoff

- [ ] T012 Update relevant Secrets inventory guidance in Studio/docs/ux/design-spec.md and/or existing Secrets module docs, keeping the shared broader filter pattern as3071 target; review changed source/tests for DRY and remove only obsolete in-scope copy.
- [ ] T013 Run complete affected @elsa-workflows/studio-secrets tests/typecheck/scopedlint, Studio lint/CSS/layout and focused secret-scopes browser checks with at most2workers; retain raw failure/skip/warning boundaries in the external handoff.
- [ ] T014 Root reviews integrated source/delegated evidence and meaningful RED→GREEN/bite-proof, then sets specs/1002-studio-secrets-inventory/spec.md to Implemented and removes the temporary checklist in the coherent implementation PR. This does not pre-claim review/CI/live acceptance.

## Dependencies and execution

T001→T002→T003→T004 precedes US1; US2 and US3 depend on accepted observation ownership and share SecretsPage/test/browser files, so one writer executes them serially. T012→T013→T014 follows the stories. Read-only root/audit review may run alongside work; no parallel source writers or overlapping heavy builds.

MVP is truthful coverage (US1), but deliver all3issueACs before accepting3069. Do not pick up Home3063, Incident3067, lists3070 or consistency3071 from this task file. No performance work or deployment.

## Mandatory integration gates outside this implementation checklist

Root owns exact-head three-axis non-author review and bounded corrections, complete affected validation, meaningful controlled revert/mutation proof, architecture restore/test/list reconciliation, generated solution-filter check, final module/app/locked host rebuilds, and normal source-bound endpoint+UI proof with101+dummy metadata rows. Prove actual page0/total/known later identity/server search, metadata-only disclosure, search/filter/detail return, themes/narrow/keyboard, served JS/CSS bytes, no unintended UI writes and full owned cleanup. Mocked fixtures are separate from actual endpoint proof. Canonical PR gates and standing merge audit precede merge; resulting-main qualification, issueACs/ProjectPassed and specific claim release follow it. Final3075 remains separate.
