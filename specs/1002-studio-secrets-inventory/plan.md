# Implementation Plan: Honest Secrets inventory coverage

**Branch**: `agent/codex/3069-secrets-inventory` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: #3069 / #3057 / #2559, winning App claim6331801398; qualified base5d1df7097db3a731330735a94c2b6a5fb7f9a476. Sole prerequisite3062 accepted. Final source integration includes separately reviewed incident head4ad8179d6496d11896bff9778ef39bb153a6cd97 without widening this leaf; its canonical verification remains separate.

## Summary

Keep the current server name/display-name search,100-record request, local metadata facets and detail dialog. Explain the completed search total versus loaded-page/displayed subset, qualify partial/filtered-empty states and offer search as the supported path beyond the first100. Bind coverage to the completed request, not draft input or a late response. No global facet semantics/layout change;3071 owns broader progressive disclosure.

## Technical Context

**Language/Version**: Existing TypeScript/React Studio pnpm workspace.
**Primary Dependencies**: Existing Secrets module, public studio-sdk/studio-ui and current HTTP client; no new dependency.
**Storage**: Existing metadata list and submitted search; no new persistence or secret-value handling.
**Testing**: Vitest observable component regressions, existing Secrets suite/typecheck, scoped lint and browser fixtures; root source-bound normal Workbench/Studio actual-endpoint proof over100 records.
**Target Platform**: Current normal composed Studio/Workbench, which already reference SecretsStudio/SecretsApi/SecretsEntityFrameworkCore.
**Project Type**: Brownfield UI correction, one existing module.
**Performance Goals**: No benchmark or invented latency target.
**Constraints**: Metadata-only, existing authorization/tenant isolation, no new backend query contract, no optional module/service setup.
**Scale/Scope**: Bounded first100; proof101 or more representative rows, below the existing separate10,000-row substring-search guard.

## Constitution Check

Both6.1.0 constitutions/Gate indexes and review checklist are unchanged from the already loaded qualified base and apply to this design. Rechecked after design; no exception.

- Framework§2.6/§2.23.4: use current supported list/query and public UI boundaries; no host-private import or recreated backend logic.
- §2.17: reuse existing test fixtures and count/request constants; avoid a generic inventory framework or trivial helper wrappers.
- §2.21.1/§2.23.2: retain observable baseline RED for misleading partial/facet-empty copy; verify complete/partial counts, submitted versus draft search, late responses and detail-return behavior. Root meaningful revert/mutation bite-proof and current-head gates remain required.
- §2.22/§2.23.3/§4.2: no public API/dependency/feature registration/event change; no catalog update indicated. Architecture listed/executed reconciliation and generated solution-filter check remain final integration gates.
- Elsa authority/disclosure rules: metadata only; no resolve-values operation, wildcard grant, raw configuration-key presentation or cross-tenant fixture. Do not widen endpoint authorization.
- No draft/provisional constitution section is needed by this work. Keep generated docs maps on their existing publication branch; do not restore deleted manifests or edit unrelated AGENTS pointers.

## Research and Design Decisions

1. Current `GET /secrets` already implements search/page/facets/filtered total. Studio passes search/activeOnly/pageSize100 and applies Type/Store/Scope/Status locally. This leaf changes coverage/copy/query ownership, not those facet semantics.
2. Count text distinguishes local displayed matches, loaded count and server total for the completed search. Partial means authoritative total exceeds actually loaded rows, not merely that100 were loaded. Preserve complete100, complete1, initial0 and filtered0 as separate cases.
3. Add a compact partial-results explanation near the list/search that says only the initial result is loaded, local filters cover it, and name/display-name search reaches other records. Avoid a new wizard/page, global inventory claims or a second list-filter pattern.
4. The input is draft until Search/Enter. Capture the query that produced each accepted result and use it for result/empty attribution. Use one narrow latest-request generation, invalidated on context/load cleanup, to prevent superseded requests from updating records/count/loading/error/dialog state. Preserve unrelated mutation semantics and descriptor handling.
5. Keep loaded-scope help, currently selected filter values, metadata-only rows and existing detail/focus lifecycle. Opening/closing detail must not reload/reset criteria. Existing mutation/create/rotate/test/revoke/delete controls/payloads are outside this correction;3071 owns operation-specific presentation consistency.
6. Source audit is an input, not runtime proof. Root will prove real endpoint first100/total/beyond100/search by exact identity on disposable normal hosts, then exercise the same search/detail-return UI against those hosts. Browser fixtures additionally cover complete/partial/error/theme/narrow edge states; do not call a mocked route a backend proof.

### Round1 clarification — retained criteria

The three independent axes found missing interaction combinations within FR-006–009. Preserve a selected scope as an explicitly qualified choice when it is absent from newly loaded metadata; this does not make descriptors or prior rows a complete catalog. Distinguish an authoritative server zero result before considering a local-facet miss. Refresh and post-mutation list reloads use the accepted query; failed-load Retry repeats its attempted query, while Search/Enter applies the input draft. Mutation targets, payloads, permissions and admission remain unchanged. These are list-context corrections, not the broader3071 filter/layout redesign.

## Project Structure

Feature artifacts: specs/1002-studio-secrets-inventory/{spec,plan,research,data-model,quickstart,tasks}.md and contracts/inventory-ui.md. A temporary requirements checklist is removed when the spec becomes terminal in the implementation PR.

Production scope: Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx, with existing module CSS only if layout evidence requires it. Preserve secretsApi.ts/secretTypes.ts unless root reconciles an actual current-contract need; no backend production change.

Tests: existing Client/src/__tests__/secretsPage.test.tsx and its shared fixtures; Studio/tests/browser/secretScopesFixture.tsx and secret-scopes.spec.ts. Reuse existing SecretsCanaryHost/read-contract tests as compatibility checks if appropriate; do not add duplicate backend tests solely for unchanged search support.

## Validation and Integration

Cost order: existing-component RED → focused observable cases/typecheck/scopedlint → root diff/DRY review → whole affected Secrets suite plus incident-independent regression exposure → three independent non-author axes and corrections → final exact-head architecture/generated-filter/client+app+locked host builds → actual normal endpoint/UI proof → canonical PR CI → standing merge audit → resulting-main qualification and criterion-by-criterion acceptance.

One implementation writer, root integration/QA. Cap client workers at2; serialize heavy.NET commands through build slots; do not rebuild/test unrelated solutions or run performance checks. Do not silently retry a deterministic failure or report skipped checks passed. PR creation may overlap canonicalCI with remaining final local/live checks only after coherent clean review and complete affected suite; every required gate is green before merge.

Normal proof uses private configuration/SQLite/encryption key/DataProtection/credentials, normal sign-in and owned processes/ports. Seed101 or more dummy values via the actual endpoint, retain only metadata/IDs/counts and assert no value/configuration-key disclosure. Confirm an exact later record absent from page0 and found by page1/server search, then normal UI search/filter/detail-close context in both themes/narrow. Bind served app/Secrets JS/CSS to reviewed source/final build bytes; confirm no unintended UI writes and complete cleanup. No backend behavior change is intended; if canonical backend mutation semantics change, stop and refine required rebuilt-server e2e rather than broadening the leaf.

Before merge: final reviewed head equals green checked head, no human hold/unresolved adopted findings/blocked label, standing authorization audit and normal branch protections. After merge: resulting-main qualification, issueACs/ProjectPassed and exact claim release. Final3075 remains an independent nine-row composed audit.
