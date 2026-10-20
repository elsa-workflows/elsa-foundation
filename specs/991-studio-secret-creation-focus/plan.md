# Implementation Plan: Keep the required source visible in Studio secret creation

**Branch**: `agent/codex/3066-secret-creation` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Approved specification for Foundation UX issue [#3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066), under epic [#3057](https://github.com/elsa-workflows/elsa-foundation/issues/3057) and Simplification program [#2559](https://github.com/elsa-workflows/elsa-foundation/issues/2559).

## Summary

Reorder the existing Secrets Create dialog so Technical name, Type, and the required Value or Configuration key lead the form. Group Display name, Description, Tags, Store, and Scope in the existing shared More options disclosure, while Expires, compact selected-store context, and the canonical Scope matching/access hint remain directly available. Preserve current creation semantics, request fields and omissions, defaults, eligibility, entered values, masking, recovery, focus, and the boundary against reading stored Secret values.

## Technical Context

**Language/Version**: TypeScript and React versions pinned in the Studio workspace; no language or runtime upgrade.

**Primary Dependencies**: Existing Secrets Client and Studio SDK. Reuse `AdvancedSection` from `@elsa-workflows/studio-ui`; the current Secrets manifest declares SDK but not Studio UI, so declare Studio UI as a direct workspace dependency and update only the Secrets importer in `Studio/pnpm-lock.yaml`.

**Storage**: N/A. No persistence or Secret-store change.

**Testing**: Secrets Client Vitest suite and build; actual-host-styles Playwright coverage for `secret-scopes.spec.ts`; Studio typecheck and lint; Studio UI package build and direct Secrets `tsc --noEmit -p tsconfig.json`; architecture and generated-map checks; configured hosted required checks and independent review.

**Target Platform**: Elsa Studio web client.

**Project Type**: Existing Studio client module with focused browser-fixture coverage.

**Performance Goals**: No new performance target, measurement, or request. The measurable viewport requirement is the entire required source control visible at 1280×800 without scrolling.

**Constraints**: Presentation-only. Preserve existing requiredness, normalization, defaults, Store and Type switching, Scope matching, expiry conversion, request shape and optional-field omissions, entered text, error and focus behavior, Show/Hide and remasking. Keep empty-Store recovery directly reachable. Preserve the no-stored-value-read boundary and do not expose authored Value or Configuration key in summaries or fixture metadata. Do not add name-generation, validation policy, provider, SDK/backend change, or public contract.

**Scale/Scope**: One existing Secret creation dialog, its focused unit tests, actual-styles browser fixture, and the direct dependency on the existing UI package. The new direct package typecheck also requires removing the obsolete ambient SDK copy and accurately typing four existing generic API test doubles and the module-registration fixture; use the current workspace SDK without changing it. Add the package typecheck script so existing workspace CI enforces this coverage. No new module or backend work.

## Constitution Check

**Preliminary review, before Phase 0**: Reviewed framework Constitution v6.1.0, Elsa Constitution v6.0.3, both Gate indexes, and [the review checklist](../../docs/contributing/review-checklist.md). This is an existing Studio client presentation change. It adds no `.Core`, feature registration, persistence, event, public API, or new project. Framework §2.21.1 requires valid existing behavior to remain covered; §2.23.2 requires behavior tests for the risky paths. §2.13 applies to the existing Studio UI package becoming a direct dependency: keep the dependency in the consuming Secrets Client and limit any lockfile change to its importer. Reuse the shared disclosure; do not create a shared helper or change the shared primitive. No unresolved clarification or applicable architectural gate failure was identified.

**Post-design review, after Phase 1**: Re-applied both constitutions and the review checklist to the chosen design. The direct package dependency is bounded to the existing UI package; no public contract or cross-feature runtime behavior is introduced. Browser and unit coverage is planned for source visibility, current request behavior, collapse/reopen, switching, advisory Scope recovery, errors, cancellation, masking, and focus. This is a plan, not test evidence: no tests, builds, browser checks, architecture checks, or hosted gates were run in this planning phase. No draft or provisional rule is used as a requirement; framework §2.24 and Elsa §§E2.8–E2.9 are not relied on by this UI change.

## Project Structure

### Documentation (this feature)

```text
specs/991-studio-secret-creation-focus/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md  # specification-quality checklist; remove at terminal status
└── tasks.md             # Phase 2 output; not created by this plan
```

There is no `contracts/` directory because this design introduces no external or public contract. The requirements checklist is authoring residue, not delivery evidence.

### Source Code (repository root)

```text
Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/
├── package.json                                      # direct studio-ui dependency
├── src/SecretDialogs.tsx                              # create form and existing request construction
├── src/SecretScopePicker.tsx                          # bounded hint association, if needed
├── src/studio-sdk.d.ts                               # remove obsolete ambient SDK copy
└── src/__tests__/
    ├── secretDialogs.test.tsx                       # creation behavior regressions
    ├── secretsApi.test.ts                           # generic fixture typing for direct tsc
    └── module.test.tsx                              # real SDK registry/API fixture

Studio/
├── pnpm-lock.yaml                                    # Secrets importer only
├── src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/
│   └── AdvancedSection.tsx                            # reuse unchanged
└── tests/browser/
    ├── secret-scopes.spec.ts                         # actual-styles viewport and recovery proof
    └── secretScopesFixture.tsx                       # metadata-only test fixture
```

**Structure Decision**: Keep the form and unit tests in the existing Secrets Client. Extend its actual-host-styles browser fixture and test for viewport, interaction, and recovery coverage. Reuse the shared `AdvancedSection` without modification. Declare the new direct `@elsa-workflows/studio-ui` import in the Secrets Client and update only that importer in the lockfile; successful hoisted or aliased resolution is not a substitute for the dependency declaration. No endpoint, SDK contract, persistence, provider, module, or backend change is needed.

## Design Decisions

1. Keep Technical name and Type first, with the selected required Value or Configuration key and Expires in the primary region. Place available descriptor descriptions after the required source so they do not displace it.
2. Group Display name, Description, Tags, Store, and Scope under the existing More options section. When the current Store is empty under existing Type-change behavior, keep Store directly reachable; Type remains directly available as the existing recovery path.
3. Keep a compact selected-Store context and the existing canonical Scope matching/access hint outside the disclosure. Render the hint once and preserve its accessibility association. If needed, use a bounded module-internal `SecretScopePicker` caller-hint association that omits only the duplicate hint; preserve current default behavior and local option/error descriptions for other callers.
4. Reuse the shared disclosure's existing closed-by-default, configured-count, manual-collapse, and forced recovery-open behavior. Count only authored optional metadata and a Store changed from its default. Do not count the default Store, Technical name, Value, Configuration key, or Expires, and do not expose any field contents in summary copy. Preserve values through manual collapse/reopen.
5. Keep Scope suggestion failure advisory. It forces the section open while the error is active; after recovery, the shared latch keeps it open until the user dismisses it. Its recovery opening must preserve a focused/authored custom Scope and must not affect creation eligibility. Do not change Store/Type defaults, requiredness, normalization, expiry, field retention, errors, Cancel/Escape, opener focus, or the Value control's remasking when recreated.
6. Preserve existing create request fields, values, conversions, timing, and omissions. Do not fetch stored Secret values; keep fixture responses metadata-only and do not echo the authored source in returned metadata.

## Implementation Sequence

1. Add an actual-host-styles browser assertion and run it RED at 1280×800 before changing production UI. It must measure the whole Value control inside the visible dialog body before any scrolling; run the matching Configuration-key path as well. Record the baseline geometry. Run the existing Secrets unit suite before production changes.
2. Add focused unit regressions for ordering, safe generic count, manual collapse/reopen, Store/Type switching, advisory Scope failure, and preserved request behavior. Run the focused assertion RED where applicable.
   After root approves the baselines, remove `src/studio-sdk.d.ts`, whose ambient declaration shadows the current workspace SDK, and accurately type the four existing generic HTTP mocks in `secretsApi.test.ts` plus the module fixture in `module.test.tsx`. Use real SDK constructors for registries and API collaborators; add the package `typecheck` script to the existing recursive CI gate. Do not add SDK exports, suppress diagnostics, or alter API behavior. Verify the compiler outcome before the layout edit.
3. Reorder the existing dialog and use `AdvancedSection`. Declare the direct Studio UI workspace dependency in the Secrets Client. Keep any picker change bounded to associating the caller-rendered canonical hint while preserving `aria-describedby`, local descriptions/errors, and default caller behavior.
4. Add only synthetic Configuration-store fixture coverage. Keep fixture reads metadata-only and assert that the authored source is absent from returned metadata. Update the tall-error browser case: Expires is now primary and visible, so scroll to a real below-fold optional field or long help while retaining body-scroll, fixed-action, error, and focus assertions.
5. Run the planned focused and full affected checks, then root-owned visual and keyboard review on the final source head. Use current semantic readiness and real geometry; do not use arbitrary sleeps or relax assertions to obtain green results.

## Validation and Lifecycle

The commands, expected evidence, and individually recorded implementation baselines are in [quickstart.md](quickstart.md). On the implementation PR's final diff, set the spec status to `Implemented` and remove `checklists/requirements.md` and its tree entry before merge. The docs-map policy now publishes the spec-status map through CI on the `generated-maps` branch after a push to `main`; documentation maps and findings are not committed or staged. The local maps `-- check` command checks only the committed dependency map. Run exact-head hosted required checks and independent review after final lifecycle edits, then verify main's post-merge CI/maps gates.
