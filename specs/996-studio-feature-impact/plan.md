# Implementation Plan: Studio Feature Impact Review

**Branch**: `agent/codex/3074-feature-impact-review` | **Date**: 2026-10-21 | **Spec**: [996-studio-feature-impact](spec.md)

**Input**: Approved feature specification from `specs/996-studio-feature-impact/spec.md`

## Summary

Add a review step to Studio Feature Management that explains the selected host's complete net draft before Apply. Keep the existing feature cascade and management contract; retain per-host direct-edit provenance, but let only effective enabled-state roots explain the current draft. If a later cascade overwrites a root's requested state, prune or mark that root inapplicable; retain independent setting edits whenever their values still differ from baseline. Show every applicable root and a cycle-safe required relationship path. Where existing cascade behavior retains an automatic state difference after its initiator is reverted, explain the earlier cause as history without claiming a pending root or direct edit. Bind the review to the host, full desired state, loaded catalog snapshot, and existing configuration revision. Invalidate it whenever the draft, target, or catalog changes. Apply only the current reviewed state. Use the public Studio authentication and permission hooks to keep authenticated readers read-only while preserving anonymous/auth-disabled demo access. No backend, SDK, dependency resolver, or persisted data changes are planned.

Implementation source is the controlled `7fb745f8604b6ed67a2eae60266976c98bce949e` base (Spec996 was approved and committed at `1d8215409fc114351ef60a03f955bfefa65975c6`). Main has since advanced to `73a4f8bf` in a separate Connections change; root owns serial integration against the current main before the final source gates. This work has no hard dependency on the concurrent trace unit.

## Technical Context

**Language/Version**: TypeScript 5.x and React 19 from the locked Studio workspace; no version changes.

**Primary Dependencies**: Existing `@elsa-workflows/studio-ui` and `@elsa-workflows/studio-sdk` workspace packages, React, and `lucide-react`; no new dependency.

**Storage**: No new persisted storage. Drafts and impact review remain client-side. A volatile module-owned completion record may retain only the successful target and safe result counts/message during the current Features visit, so same-version registry remounts do not erase the outcome. It carries no draft, payload, settings values or authorization data and clears on deliberate context/draft actions or route departure. Apply continues to persist through the existing selected-host feature management behavior.

**Testing**: Existing Vitest and jsdom module suite; existing Modularity service/configuration-store tests as backend preservation checks; actual private Studio.Web plus Workbench browser proof.

**Target Platform**: The Feature Management extension in the browser, hosted by Elsa.Studio.Web and addressing its Studio or Workbench Server target.

**Project Type**: Existing Studio extension UI within the Studio pnpm workspace, with no new project or server module.

**Performance Goals**: None specified. Do not add timing, throughput, or scale claims.

**Constraints**: Preserve the existing full-state Apply contract, required/optional dependency semantics, selected-host isolation, and backend authority. Do not change the API, SDK, backend, permission policy, provider composition, host configuration, lockfiles, or deployment behavior. Keep secrets out of test output and browser receipts. Build and test sequentially through the repository's normal wrappers; root owns shared-machine heavy gates.

**Scale/Scope**: One existing Studio module, its component/unit tests, and its scoped stylesheet if needed. Remove its obsolete local SDK declaration shim so the module consumes the existing public SDK types. No new shared service or cross-host management surface.

## Constitution Check

Both constitutions were loaded: Framework v6.1.0 and Elsa v6.0.3. Their ratification notes exclude draft framework §2.24 and provisional Elsa §E2.9 from ratification; neither section is needed for this work.

| Gate | Assessment |
|---|---|
| Framework §2.17 — duplication beats dependency | Pass. Reuse current module and SDK packages; add no helper package or runtime dependency. |
| Framework §2.23.1 — feature composition | Pass. No feature registration or composition changes; preserve and test the current registration behavior. |
| Framework §2.23.2 — behavior tests | Pass. Add behavior-level tests for cascade provenance, filtered impact, review invalidation, target isolation, permission states, and failure display. Do not assert source text or duplicate backend side effects in test doubles. |
| Framework §2.23.3 — public surface | Pass. No new public package contract or project. Keep new data types private to the feature module. |
| Framework §2.23.4 — hidden coupling | Pass. Use the Studio SDK's public auth/session and permission hooks; do not depend on Elsa.Studio.Web's app-private host-control hook. |
| Elsa §E2.2.3 — deployment shapes | Pass. No runtime/design package or host composition changes; qualify the actual Studio and Workbench hosts to preserve both host boundaries. |
| Review checklist — scoped change and truthful evidence | Pass with the focused module and service-preservation tests, private host proof, serial host builds, architecture/map checks, and root-owned exact-head/final-main gates below. |

No constitution exception or complexity justification is needed.

## Project Structure

### Documentation (this feature)

```text
specs/996-studio-feature-impact/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── impact-review.md
```

`tasks.md` is reserved for the later, separately reviewed Speckit tasks phase and is not created in this plan checkpoint.

### Source Code (repository root)

```text
Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/
├── module.tsx                  # host-scoped review, provenance, permissions, invalidation, Apply outcome
├── styles.css                  # scoped review layout/states if the selected UI presentation needs new styling
└── __tests__/
    └── module.test.ts          # shared fixtures and behavior-level unit/component coverage

Essentials/tests/Modularity/Tests/
├── FeatureManagementServiceTests.cs             # existing preservation suite; no edits planned
└── JsonShellFeatureConfigurationStoreTests.cs   # existing revision/persistence preservation suite; no edits planned
```

The current Studio SDK already publicly re-exports `useAuthSession`, `usePermissions`, the auth providers, and the dialog focus hook. The module's obsolete local `src/studio-sdk.d.ts` declaration shadows those actual exports and is removed rather than maintaining a second SDK contract. No SDK package source or declaration, backend project, route, API contract, package manifest, lockfile, or host configuration file is in the planned product diff. Root authorized this narrow scope correction after the direct module TypeScript check exposed the shadowing.

**Structure Decision**: Extend the existing Feature Management module and its `module.test.ts`, with scoped styles only for the new review surface. Keep existing cascade calculation and Apply transport in place. Use existing Studio SDK auth exports and existing backend behavior without adding a package, service, route, or persisted entity.

## Complexity Tracking

No constitution violations or added projects/dependencies.

## Root refinement from actual host proof

The integrated940 host run proved both reviewed requests changed only their selected private shell file, but Studio's modules-changed event rebuilt the registry and discarded the selected target and completion notice. FR-019 closes that observed presentation gap without persisting a draft or changing the SDK. Do not destructively consume a one-shot record in a React initializer: StrictMode and repeated same-route registry rebuilds must remain safe. The host can rebuild once for its explicit revision and again when its manifest refetch completes. Retain a completion only for the current Features visit and verify the actual same-version remount behavior. Observe actual route departure while the page is temporarily absent in the host loading shell; a component-unmount cleanup alone cannot cover that gap. Use only a bounded module-owned navigation observer while an Apply is pending or a completion exists, with cleanup when neither remains; do not add host/SDK state services. Existing shared success feedback is reused and focused; the disabled review opener is not the post-success focus target. Capture the current visit for an Apply before awaiting it: a response arriving after departure and return must neither restore an old completion nor erase a newer visit’s result. New rendered regressions precede this bounded correction, and the actual two-host proof is repeated afterward.

## Root Plan Review

Root read all five design artifacts against the approved specification, issue acceptance and source-readiness record. Independent planning R1 identified stale roots after a later cascade overwrites their requested state; the revised effective-root rule, independent setting provenance and exact A/B regression resolve that finding. Independent bounded R2 is clean. The validation guide now distinguishes baseline observation from the new review acceptance and preserves actual two-host Cancel/Apply proof.

Approved for official task generation only. No product edit or implementation gate is complete. Root retains safe private Apply selection, integration, shared-machine qualification and final acceptance.

## Implementation qualification

The final product checkpoint was integrated with current main da9 at6b287aec5 and passed the prescribed serial affected-client, preservation, workspace, host, architecture and map gates. Actual private normal-host review/Cancel/correct-file Apply, completion/remount/focus/departure and cleanup are accepted with their source boundaries in the external `3074-private-host-proof.md`. The terminal spec status is included in PR3155 per the lifecycle rule; final exact-head review/CI and resulting-main qualification remain open in tasks.md. The existing parent inspector responsive defect is separately owned by #3154 and is not attributed to the new review dialog.
