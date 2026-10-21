# Implementation Plan: Studio Narrow Header Reflow

**Branch**: `agent/codex/3165-narrow-header-actions` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification drafted for #3165 / #3057 / #2559.

## Summary

Correct the current-main Studio shell overflow confirmed by rendered geometry. The fixture reproduces a clipped account hit target and horizontal overflow at 320px and 360px. Root's earlier normal-host locator-driven account-menu activations measured frame scroll 0→106px/content x=16→−90px at 320px and 0→79px/x=16→−63px at 360px; because Playwright locator activation can scroll an offscreen center into view, these are automation observations rather than native-pointer proof. Final candidate native-input proof is linked below; baseline locator movement still does not establish native-pointer causality. A visible-point physical pointer click in the component fixture does not scroll, while the full target remains clipped. The tested current-main baseline is `a710adf26ad16ff9fe7fbbd1db5818c604aee45d`; its header CSS is unchanged from the root-measured `4ca6025872966914f15a34bf795a317f0e112081` candidate baseline. Clicking the Light control at 360px did not shift the current-main page, so the historical trigger is retained as negative evidence. Root compared candidate layouts against identical current-main feature-page screenshots and selected a combined CSS-only density/reflow correction.

The `.topbar-actions` wrapping and the status wrapping below follow the existing `max-width:1024px` stacked-shell breakpoint. That shell column uses `minmax(0, 1fr)` so top-navigation status content cannot impose a minimum wider than the viewport; the duplicate phone-width column override is removed. At `max-width:640px`, the selected candidate hides only the redundant `.theme-mode-toggle` quick strip while leaving the palette selector directly visible, and hides `.user-menu-name` while presenting the existing account trigger as a centered 40px avatar button. Root’s subsequent dense-status fixture measured badges extending to x=606px despite the Weaver button fitting within x=16..304px. The selected correction therefore also allows its status indicator to wrap and the launcher height to grow above its existing 36px minimum. These adjustments keep Weaver, appearance, account, and status information reachable. Restore the quick strip and labeled account at 641px and above; preserve the desktop layout above 1024px. Keep the change in `Studio/src/apps/Elsa.Studio.Web/Client/src/app/styles.css`; no production TSX, component stylesheet, API, or shared primitive change is selected.

Root's in-memory comparison measured wrap-only at 320px as a 215px header/136px action row and at 360px as 165px/86px; quick-mode compaction alone measured 169px/90px at both widths. The combined selected layout measured a 119px header/40px action row without overflow. Account activation stayed at x=16 with no scroll at both tested widths. The palette menu remained 288px wide within the 320px viewport and retained all four mode and three navigation choices; Escape restored focus to the selector. At 640px compact presentation remains, while at 641px quick modes and labeled account return. The 1280px/1440px desktop controls and sidebar account remain unchanged. These are root's current-main option-selection receipts, not final post-change source proof; final normal-host physical-pointer and keyboard proof is recorded below.

## Technical Context

**Language/Version**: TypeScript 5.x / React 19; CSS in the existing Studio workspace.

**Primary Dependencies**: Existing React, Radix-backed Studio controls, and `@playwright/test`; no dependency changes.

**Storage**: None.

**Testing**: Existing Studio browser test `Studio/tests/browser/user-menu.spec.ts` and its real-shell fixture `Studio/tests/browser/userMenuFixture.tsx`; focused existing Client Vitest tests for ThemeSwitcher, UserMenu, appearance/navigation preferences, and AgentLauncher as applicable.

**Target Platform**: Studio browser shell in normal composed `Elsa.Studio.Web` host.

**Project Type**: Existing Studio web application.

**Performance Goals**: None.

**Constraints**: Preserve account/auth behavior, native source anchors, Weaver state/status, all existing appearance and navigation choices, keyboard order, selected state, Escape and focus restoration. No new menu/API/shared disclosure contract, backend or permission change, dependency, lockfile, host configuration or deployment work. Keep desktop quick controls and sidebar account presentation. Do not hide overflow or add a horizontal action scroller.

**Scale/Scope**: One shell responsive correction and focused regression evidence. No backend behavior.

## Constitution Check

Both constitutions and the review checklist were consulted. Framework v6.1.0 governs architecture and tests; Elsa v6.1.0 adds no gate relevant to this CSS-only shell change. No draft/provisional constitutional section is implicated.

| Gate | Assessment |
|---|---|
| Framework §2.17 — duplication beats dependency | Pass. Reuse the existing shell controls and palette menu; add no shared package or dependency. |
| Framework §2.21.1 — preserve valid behavior | Pass if current appearance, navigation, account, anchor, and Weaver behaviors remain valid and covered by existing tests. |
| Framework §2.23.2 — test risky behavior | Applicable to the responsive regression: prove overflow/position behavior through rendered geometry and pointer/focus interactions; do not test CSS text or declarations. |
| Framework §2.23.3 — public surface | No public API or package surface planned. |
| Elsa gates | No Elsa-specific gate applies; no runtime, package or host composition changes. |
| Review checklist — scoped, source-grounded evidence | Pass when the normal composed host baseline, matched candidate evidence, focused regression, exact-head gates, and evidence provenance are recorded separately. |

No exception or complexity justification is required.

## Project Structure

### Documentation

```text
specs/997-studio-narrow-header-reflow/
├── spec.md
├── plan.md
└── tasks.md
```

These are the canonical work-unit documents. Root selected the design after current-main option comparison; the final post-change host proof is recorded below.

### Likely Product and Test Files

```text
Studio/src/apps/Elsa.Studio.Web/Client/src/app/styles.css
Studio/tests/browser/userMenuFixture.tsx
Studio/tests/browser/user-menu.spec.ts
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/themes.test.ts          # if selector behavior needs preservation coverage
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/appearance.test.ts      # if preferences behavior needs preservation coverage
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/user-menu.test.tsx      # if account behavior is touched
Studio/src/apps/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentLauncher.test.tsx # if Weaver markup/behavior is touched
```

Expected product edit is CSS-only in `app/styles.css`, including the selected action/status reflow and compact controls. Extend the existing real-shell fixture/spec rather than copying ShellFrame. The fixture uses query-controlled navigation, identity, appearance availability, account availability, and Weaver state, and the actual `AgentLauncher` with deterministic session-indicator data and local toggle state. The focused geometry test on current CSS at `a710adf26ad16ff9fe7fbbd1db5818c604aee45d`, with long `Administrator` identity and actual Weaver session status, measured: at 320px, actions 288/419 and frame 320/435, account x=293.27..435.19; at 360px, actions 328/445 and frame 360/461, account x=318.77..460.69. A physical pointer click on the visible portion of the overflowing target and Escape left fixture `scrollLeft=0` and content x=0 at both widths. Thus the fixture RED is the measured clipped hit target and horizontal overflow; it does not reproduce the normal-host click movement. Root's baseline locator-driven host movement remains an automation observation, separate from the final candidate's native-input proof.

## Fixture RED Evidence

The focused assertion measures the account button rectangle, action/frame `clientWidth` and `scrollWidth`, frame `scrollLeft`, and content left coordinate. On the unchanged product CSS it fails `accountWithinViewport`, `actionsHaveNoHorizontalOverflow`, and `frameHasNoHorizontalOverflow` at 320px and 360px. It also clicks a point in the visible part of the account control, opens the real account menu, closes with Escape, and records that this fixture path itself preserves `scrollLeft=0` and content x=0. This distinction is deliberate: root's normal-host locator observations include helper-assisted scrolling; the existing component fixture proves the header target and overflow geometry without attributing Playwright's internal scroll-into-view behavior to product code. The command was `cd Studio && pnpm exec playwright test tests/browser/user-menu.spec.ts --workers=1 --grep 'account action fits and preserves shell position'`; it exited 1 with the two expected baseline assertion failures. Its screenshots, geometry JSON and traces are in ignored `Studio/test-results/` outputs for this local review; no source CSS was changed.

## Implementation Sequence

1. Root recorded current-main normal-host account-menu locator observations at 320px and 360px; baseline native-pointer/keyboard attribution remains unproven. The current-main Light click at 360px did not shift the page and is recorded as negative evidence.
2. Root has compared wrapping, quick-mode compaction, and narrow avatar-account density on matched current-main options and selected the combined CSS change documented above.
3. Extend `userMenuFixture.tsx` and `user-menu.spec.ts` for the account-target overflow regression using actual rendered geometry, retaining the real auth boundary and ShellFrame. Add the actual `AgentLauncher` with deterministic session status and local open/close state to exercise width and activation without backend calls; allow appearance and account actions to be absent without changing auth behavior; do not duplicate ShellFrame.
4. After the geometry regression fails on the unchanged product CSS baseline and root review selects the correction, implement only the selected responsive adjustment in `app/styles.css`. Keep source order and semantics; add no menu abstraction.
5. Run focused browser and affected Client tests, lint and bundle, then root-owned normal-host after proof and merge gates. Bite-proof by reverting the correction in a disposable candidate build or equivalent controlled mutation and showing the account-target or status-badge overflow assertion fails again.

## Verification Commands

After confirming the existing Playwright fixture server/config in the repository:

```bash
cd Studio
pnpm exec playwright test tests/browser/user-menu.spec.ts
```

Focused Client test and build, if affected:

```bash
cd Studio/src/apps/Elsa.Studio.Web/Client
pnpm exec vitest run <selected-test-path>
pnpm build
```

Relevant workspace checks from `Studio/` are `pnpm lint:css`, and `pnpm lint` / `pnpm typecheck` if the fixture or TypeScript files change. Host builds and browser proof remain root-owned and sequential under the documented private-root procedure. No backend e2e test is applicable.

## Risks and Open Decisions

- The historical Light-control trigger did not reproduce on current main at 360px. Account-target clipping is the confirmed current-main defect; locator-assisted account scrolling does not prove native-pointer causality. Preserve both facts in the final report.
- The compact account label is chosen for header density, but identity must remain available through the button accessible name and account menu; verify this by keyboard and pointer.
- The responsive fixture now has a `weaver=dense` variant combining active, waiting, blocked, failed, proposal, artifact, prompt, and child-session badges; its 320px geometry/accessibility case passes in root’s serial browser run. `AgentSessionIndicator` can compose other status counts and nesting shapes, so do not claim exhaustive combinations. Root measured dense launcher overflow, selected wrapping within the existing indicator and an automatically sized launcher, and verified both horizontal and vertical badge containment. The responsive button selector also wins over the later base launcher height rule. This is a component robustness case; the normal composed host currently emits a single session with active/waiting/failed status plus proposal and artifact counts, rather than every badge category together.
- The bounded High contrast, reduced-motion, authenticated/anonymous, Weaver/session, and navigation-mode coverage below is not broad accessibility certification.

## Complexity Tracking

No new dependency, project, API, contract, shared primitive, host setting, or constitutional exception.

## Root verification checkpoint

The current candidate uses stylesheet SHA256 `2b6de8b139bd151c5a2e11737c390f6af0d3c8ed3bf066a6da89a7aebd78d9aa`. All19 serial browser cases pass with0 skipped/flaky, including long backend hostnames at320/360, exact shell scroll widths and full frame/topbar/status-bar/control bounds, both navigation input paths, dense badge containment, breakpoints and desktop account bounds. All88 tests across six affected Client files pass. Workspace lint (54 warnings/0 errors), typecheck, Client bundle and final Studio host build pass. This is bounded shell coverage, not all-module qualification.

A fresh normal composed Workbench/Studio run passes14 recorded rows: normal anonymous redirect and administrator sign-in; physical account input; native Tab/Enter, Escape and focus restoration in320 Light/left,360 Dark/top,320 Dark/top and320 High contrast/adaptive; all eight themes and Meridian-to-Classic selection; six width boundaries/desktop references; and focused640-to-641 resizing. The stronger host harness checks shell scroll width and frame/topbar/status-bar bounds too. Tested native input preserves module x16/frame scroll0. All36 loaded assets were available and the three app assets hash-match the rebuilt output. Zero page errors, nine GET agent bootstrap requests and no agent POST/turns. Both owned hosts/browser exited, all12 private config hashes stayed unchanged, private roots/profiles were removed and5689/5695 are reusable.

This host proof is content-bound to the changed stylesheet and the be098 branch source before the coherent fix commit. The Workbench binary retains its rebuilt a710 identity; no C# changed in this slice. Studio frontend/host were rebuilt for the current stylesheet. It is not a whole-host rebuild from the subsequent commit or current main882; resulting-main qualification remains separate. The earlier13-row proof applies only to its b13 stylesheet. [Current root qualification and source/cleanup bounds](https://github.com/elsa-workflows/elsa-foundation/issues/3165#issuecomment-6315013859).

## Regression and review history

The original a710 CSS fails the320/360 account-target assertions. A dense status case measured badges extending to x606 despite the launcher fitting x16..304; a per-control check also exposed a641px control at x=-58.9. Root moved action/status wrapping to the existing1024px stacked-shell breakpoint, retaining compact controls through640px. Visual review then caught a later base rule retaining36px height: the vertical containment assertion fails with badge top95.4 above button top119.8 and passes when the responsive button selector wins. The first candidate's16-case result and13-row host receipt use b13b0989. [Initial qualification](https://github.com/elsa-workflows/elsa-foundation/issues/3165#issuecomment-6313772100) retains that historical scope.

Round1 on6651 found no correctness defect, an optional duplication note and a P2 keyboard-selection coverage gap. The be098 follow-up reuses geometry assertions, adds keyboard Dark/Top/Meridian/Classic selection and sign-out, and bounds the desktop account target at1280/1440. Its17 serial browser cases passed locally, and three independent R2 reviews found no actionable code/acceptance defect. The wording note about the earlier16-case receipt is clarified here.

Hosted Studio39585308669 at6651 failed a320px navigation-change assertion with an unidentified control ending at327px. The font/two-frame settling follow-up did not resolve it: Studio39588049965 at be098 executed352 passes,5 skips and2 failures in the pointer and keyboard Top-selection paths. Diagnostics identify account right327, fonts loaded, body320px with zero margin/padding, shell rect/client320 but scrollWidth343, and frame/topbar343. Timing and modal scroll-lock compensation were hypotheses, not established causes.

The final long-backend fixture reproduces the inner track expansion locally: shell320/518 and frame/topbar/status bar518px. The responsive bare1fr column takes an automatic content minimum from the top-navigation status bar. Changing only that column to minmax(0,1fr), and removing its redundant640px override, returns shell/frame/status widths to320 and account right304. Restoring the b13 stylesheet makes both final320/360 long-host tests fail at shell scrollWidth518; restoring the current stylesheet preserves its exact hash and19-case passing source. No overflow mask, tolerance or timeout was added. Existing status text retains its original shrinking behavior within the constrained grid.

Initial fixture expectations for exact High contrast appearance and plural status wording were corrected without product changes. The long-host trial first used an accessibility-role locator while the open modal intentionally hid its background from that tree; the DOM locator correction preceded the recorded geometry RED. The mutation summary parser initially mishandled reporter formatting; the raw report confirms two geometry failures. None of those harness mistakes is counted as a product regression. Locator-assisted baseline scroll offsets remain automation observations, not native-pointer causality.

The coherent fix commit, third independent exact-head reviews, fresh hosted architecture/maps/CI and resulting-main qualification remain integration gates. Final3075 remains staged. The terminal spec status in this implementation PR becomes true on merge.
