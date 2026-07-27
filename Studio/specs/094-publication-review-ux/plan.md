# Implementation Plan: Workflow Publication Review UX

**Branch**: `codex/publication-review-ux` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/094-publication-review-ux/spec.md`

## Summary

Rebuild the Workflows module's publication-review dialog as a calm, single-page decision surface. It will keep the publication context and actions visible, make the selected **Publication channel** and its create-or-replace effect primary, show policy-assigned version/readiness/baseline and a compact change summary, and move evidence into progressive disclosures. The existing save → promote → token-bound activation lifecycle, policy authority, and concurrency protections stay intact. The client will automatically refresh authoritative preflight evidence when the channel or requested version changes. Exact Semantic Version editing is an additive, capability-gated Foundation integration; the redesigned automatic-version path must work unchanged when the relation is absent.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19

**Primary Dependencies**: `@elsa-workflows/studio-sdk`, `@elsa-workflows/studio-ui`, existing Workflows publication API/client, Lucide React, React DOM test harness

**Storage**: Existing workflow draft, promoted-version, publication, Source Reference, and Executable persistence owned by Foundation; no new Studio persistence

**Testing**: Vitest 2 component/model/operation tests; Playwright 1.61 browser tests and visual review; `pnpm lint:css`, Workflows typecheck/build

**Target Platform**: Modern desktop browsers supported by Elsa Foundation Studio, including constrained viewport heights and keyboard/screen-reader operation

**Project Type**: Modular browser application with capability-discovered Foundation APIs

**Performance Goals**: Opening a ready review keeps the existing one initial authoritative preflight; changed channel/version requests are debounced or superseded so only the latest result can re-enable Publish; no additional long-lived polling or persisted client state

**Constraints**: Preserve the captured-draft save semantics, publication permissions, host/workflow policy authority, expected-publication concurrency protection, token-bound activation, retry-without-repromotion behavior, existing retained Executable/Source Reference semantics, and backwards compatibility with Foundation deployments without exact-version capability. Module CSS uses only the `--studio-*` contract and existing `--wf-*` aliases backed by it.

**Scale/Scope**: One workflow-authoring publication dialog and its review model/operation tests; existing capability client/types; existing browser fixture. A companion Foundation contract change is required only for the optional exact-version path.

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Modular UI contract — PASS**: The Workflows module keeps publication composition local and consumes existing dialog/focus primitives. It does not introduce a new Studio Slot, Contribution, or duplicate a host-owned primitive. The optional Foundation capability is discovered through the existing API capability document rather than a hard-coded server version.
- **Workbench pattern fit — PASS**: Publication remains a focused modal inside the workflow designer configuration workbench. The redesign simplifies its decision hierarchy; it does not turn publication into a wizard or separate page.
- **Typography and token discipline — PASS**: The existing `wf-*` aliases and documented `--studio-*` semantic/status/surface tokens provide all surfaces, borders, focus, and state treatments. No raw color, font, radius, shadow, or unmanaged status rule is planned.
- **Accessible interaction — PASS**: The plan specifies focus entry/return, Escape behavior, labelled controls, keyboard-operable combobox/create path and disclosures, live checking/status/error announcements, and a footer that remains in the visible modal at every state.
- **Real-screen proof — PASS**: The existing real Workflows browser fixture will exercise normal, occupied named, and new named channels; long success/error content; a constrained-height modal; keyboard operation; and outcome actions.

**Post-design re-check**: PASS. [data-model.md](./data-model.md) defines the client state and state transitions, [workflow publication review contract](./contracts/workflow-publication-review.md) defines interaction/accessibility/compatibility boundaries, and [quickstart.md](./quickstart.md) defines browser proof. The Foundation capability relation is explicitly isolated and does not cause a compatibility break.

## Delivery Phases

### Phase 0 — Research consolidation

1. Trace the current dialog, its document model, publication-preflight client, save/promote/activate flow, and existing focused tests.
2. Confirm the overflow cause: the entire form scrolls, including actions, so appended outcome/alert content displaces the only controls.
3. Identify the current policy-derived `slotName`/`action` intent and expected-publication guard, and preserve their server authority while replacing their presentation.
4. Record the capability-discovery approach for the optional Foundation exact-version extension.

Output: [research.md](./research.md)

### Phase 1 — Interaction design and contracts

1. Define publication-review state, channel selection, authoritative evidence, version-mode, outcome/recovery, and stale-result transitions.
2. Define the visible hierarchy, progressive disclosure, persistent shell, and accessibility contract.
3. Define the capability-gated Workflow Design relations for promotion-version preflight and requested exact versions, including validation and compatibility rules.
4. Define component, operation, and browser validation scenarios.

Outputs: [data-model.md](./data-model.md), [workflow publication review contract](./contracts/workflow-publication-review.md), [quickstart.md](./quickstart.md)

### Phase 2 — Test-first Studio implementation

1. Add failing model/component tests for channel options/effects/baselines, policy-assigned version labels, hidden technical evidence, automatic latest-only preflight, and each outcome/recovery state.
2. Extend the publication-review model with a user-facing channel selection and version mode while retaining the wire-level `PublicationIntent` projection and concurrency data.
3. Recompose `PublicationReviewDialog` into persistent header, independently scrollable body, and persistent footer; replace radio/free-text Slot controls with existing-channel selection plus a distinct create path.
4. Make channel/version mutations invalidate readiness and automatically call Foundation's promotion/version preflight for the latest selection; automatic mode renders the server-policy-assigned proposal, while exact mode includes the requested version only when capability support is advertised. Publish remains labelled Publish and stays disabled while evidence is checking or invalid.
5. Render compact success and retained-promotion recovery bodies rather than appending alerts below review content; expose Open published executable through the existing executable navigation path.
6. Add optional exact-version UI/client fields only behind the discovered Foundation capability; retain the automatic path as the default and sole path for older deployments.
7. Add token-based layout styles for the fixed shell, responsive summary, disclosures, selector, and status treatments.

### Phase 3 — Foundation exact-version companion delivery

1. Add an additive capability relation in Foundation that advertises exact-version support only where validation and promotion both honor it.
2. Extend authoritative preflight and promotion/activation contracts with a requested exact SemVer and an unambiguous server-confirmed version, preserving review-token and stale-target validation.
3. Enforce valid, unused, precedence-forward Semantic Versions (including forward prereleases), reject duplicate/downgrade/invalid values before promotion, and retain existing permission and policy checks.
4. Publish the relation only after server-side integration tests cover automatic and exact-version paths. Studio integration tests then exercise both capability-present and capability-absent forms.

### Phase 4 — Verification and landing

1. Run focused model/component/operation tests and Workflows typecheck/build.
2. Run CSS lint plus repository-required lint/test gates.
3. Run the focused Playwright scenarios at normal and constrained modal heights; inspect screenshots in the themed real Studio screen.
4. Independently review all state transitions, stale-result behavior, keyboard/focus flow, and Foundation-absent compatibility before landing.

## Project Structure

### Documentation

```text
specs/094-publication-review-ux/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── workflow-publication-review.md
├── checklists/
│   └── requirements.md
└── tasks.md                    # Created by /speckit-tasks, not this plan
```

### Source Code

```text
src/Elsa.Studio.Workflows/Client/src/
├── api/
│   ├── capabilities.ts
│   └── publishing.ts
├── workflow-editor/
│   ├── WorkflowEditor.tsx
│   ├── publicationReview.ts
│   └── useWorkflowOperations.ts
├── styles.css
└── __tests__/
    ├── publicationReview.test.ts
    ├── publicationSlots.test.tsx
    └── workflowPublicationOperations.test.tsx

tests/browser/
└── workflow-publication-review.spec.ts (or the existing Workflows publication fixture)
```

**Structure Decision**: Keep state derivation and intent projection in `publicationReview.ts`, orchestration/retry/latest-request control in `useWorkflowOperations.ts`, dialog composition in `WorkflowEditor.tsx`, and module-local token-based styling in `styles.css`. Reuse the current focused publication test suites; browser proof extends the existing Workflows fixture rather than introducing a second host application. The companion Foundation server implementation lives outside this repository and is described only as an additive consumer contract here.

## Integration Order

1. Focused tests and deterministic preflight request control.
2. Publication-review state/intent derivation and Foundation-capability parsing.
3. Dialog hierarchy, channel selector/create flow, progressive disclosures, and fixed layout shell.
4. Success/recovery actions and executable navigation.
5. Exact-version capability path and absent-capability compatibility tests.
6. Browser fixture, constrained-height proof, CSS lint, typecheck/build, and screenshot review.
7. Foundation companion integration when its capability is available.

## Risk Controls

- Keep Foundation as the authority for action resolution, channel rules, trigger conflicts, version validity, policy, and final version; Studio renders but does not infer those outcomes.
- Associate every asynchronous preflight result with the current immutable review snapshot, normalized channel selection, exact-version request (if any), and request generation; discard late results.
- Keep `expectedPublicationId` and the review/preflight token in the final activation request so a changed channel cannot be silently overwritten.
- Treat an occupied target selected through either list path as replacement, including a race where the create path becomes occupied before review completes.
- Preserve the already-promoted version ID after activation failure and retry activation only; never re-save or re-promote it.
- Separate the scroll body from header/footer structurally, not by relying on a success alert's height, so all modal states preserve reachable actions.
- Do not display exact-version controls unless Foundation explicitly advertises end-to-end support; automatic policy assignment remains functional without it.
- Verify the actual Workflows modal with browser screenshots because component tests alone cannot prove visible fixed controls or viewport overflow.

## Complexity Tracking

No constitution violations require justification. The cross-repository exact-version capability is a delivery dependency, not a Studio architecture exception.
