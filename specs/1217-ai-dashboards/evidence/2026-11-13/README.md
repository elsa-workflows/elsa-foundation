# Dashboard delivery evidence — 13 November 2026

Status: local Home and live Copilot qualification and independent visual review passed. Final native/comparison, ordered merges and resulting-main verification remain in progress.

The screenshots below are unmodified originals from owned, disposable composed hosts using synthetic qualification data. An independent acceptance subagent actually opened all 40. [The manifest](screenshots.json) records their exact tested source, original run filename and SHA-256. These retained files provide downloadable evidence while Actions is delayed. Raw traces, browser storage, fixture tokens and backend/provider logs stay private.

| Witness | Tested source | Result |
| --- | --- | --- |
| Existing Home widgets/layout/defaults | `6f7db4b23dd22891edb2523eb08b0912ead90ad5` | 4/4 browser profiles, 59/59 field gallery; 20 actual PNGs independently inspected |
| Actual Copilot authoring over Weaver | `c56b64e365bffc775ba2034655f290f985a1c2cc` | 2/2 real Workflow/Acme journeys; 20 actual PNGs independently inspected |
| Independent local package contribution | `11c48db71b17972771507f5f1b4da2238975a0e0` | 20 archives, 19/19 native cases, offline package-only restore, no frontend calls |

[Home receipt](home-receipt.json), [live receipt](live-receipt.json), [allowlisted package receipt](package-witness.json).

Live qualification uses the existing GitHub Copilot SDK adapter (SDK 1.0.4, explicitly selected `gpt-6.1-sol`) through model-agnostic Weaver Dashboard authoring. Actual SDK sends are Workflow 0→2→3→3 and Acme 3→5→6→6. All native phases have zero additional sends; privacy-failure counters remain zero. Acme independently recomputes reader Retail 60000 versus owner 120000, with Enterprise 300000 unchanged. After permission removal and ordinary sign-in, the new reader principal receives 403 and no query/result disclosure. This does not qualify invalidation of outstanding signed tokens.

The later integration changes add only the Architecture project lock entries and align unpublished Acme package/API-tracking metadata. Runtime C#/TypeScript/CSS behavior is unchanged from these tested sources. Native/comparison evidence will name its own committed final source.

## Independently inspected originals

### home-desktop-light

[home-widgets](screenshots/home-desktop-light--home-widgets.png) | [home-module-settings-reopened](screenshots/home-desktop-light--home-module-settings-reopened.png) | [home-server-default](screenshots/home-desktop-light--home-server-default.png) | [home-module-portfolio](screenshots/home-desktop-light--home-module-portfolio.png) | [home-populated-appearance-1](screenshots/home-desktop-light--home-populated-appearance-1.png) | [home-populated-appearance-2](screenshots/home-desktop-light--home-populated-appearance-2.png)

### home-desktop-dark

[home-layout-reopened](screenshots/home-desktop-dark--home-layout-reopened.png) | [home-module-picker-1](screenshots/home-desktop-dark--home-module-picker-1.png) | [home-populated-appearance-1](screenshots/home-desktop-dark--home-populated-appearance-1.png) | [home-populated-appearance-2](screenshots/home-desktop-dark--home-populated-appearance-2.png)

### home-narrow-light

[home-widgets](screenshots/home-narrow-light--home-widgets.png) | [home-widget-picker](screenshots/home-narrow-light--home-widget-picker.png) | [home-module-portfolio](screenshots/home-narrow-light--home-module-portfolio.png) | [home-populated-appearance-1](screenshots/home-narrow-light--home-populated-appearance-1.png) | [home-populated-appearance-2](screenshots/home-narrow-light--home-populated-appearance-2.png)

### home-narrow-dark

[home-layout-reopened](screenshots/home-narrow-dark--home-layout-reopened.png) | [home-module-settings-reopened](screenshots/home-narrow-dark--home-module-settings-reopened.png) | [home-module-run-health](screenshots/home-narrow-dark--home-module-run-health.png) | [home-populated-appearance-1](screenshots/home-narrow-dark--home-populated-appearance-1.png) | [home-populated-appearance-2](screenshots/home-narrow-dark--home-populated-appearance-2.png)

### live-workflow

[live-clarification](screenshots/live-workflow--live-clarification.png) | [live-proposal-preview-values](screenshots/live-workflow--live-proposal-preview-values.png) | [live-refined-preview-chart](screenshots/live-workflow--live-refined-preview-chart.png) | [live-saved-light-desktop-values](screenshots/live-workflow--live-saved-light-desktop-values.png) | [live-saved-dark-desktop-values](screenshots/live-workflow--live-saved-dark-desktop-values.png) | [live-saved-light-narrow-values](screenshots/live-workflow--live-saved-light-narrow-values.png) | [live-saved-dark-narrow-values-end](screenshots/live-workflow--live-saved-dark-narrow-values-end.png) | [live-refreshed](screenshots/live-workflow--live-refreshed.png) | [live-drill](screenshots/live-workflow--live-drill.png)

### live-acme

[live-clarification](screenshots/live-acme--live-clarification.png) | [live-proposal-preview-values](screenshots/live-acme--live-proposal-preview-values.png) | [live-refined-preview-values](screenshots/live-acme--live-refined-preview-values.png) | [live-saved-light-desktop-values](screenshots/live-acme--live-saved-light-desktop-values.png) | [live-saved-dark-desktop-values](screenshots/live-acme--live-saved-dark-desktop-values.png) | [live-saved-light-narrow-values](screenshots/live-acme--live-saved-light-narrow-values.png) | [live-saved-dark-narrow-values-end](screenshots/live-acme--live-saved-dark-narrow-values-end.png) | [live-shared-reader-values](screenshots/live-acme--live-shared-reader-values.png) | [live-reader-no-category](screenshots/live-acme--live-reader-no-category.png) | [live-applied-draft](screenshots/live-acme--live-applied-draft.png) | [live-share](screenshots/live-acme--live-share.png)

## Failed and superseded evidence

Earlier comparison assertions expected `current.total` although native output uses `total`; the corrected whole journey passed all four profiles. Earlier comparison screenshots put numeric rows below a scrolling viewport; current aligned captures are separately requalified. A first resumed Acme live run reused an old signed principal after changing IAM grants; the corrected flow signs in again and requires a direct native 403. These earlier failures remain failures.

A Home attempt stopped during field-gallery setup because port 4197 was occupied and ran no Home journey. The runner now allocates a port and records its URL only when the selected frontend confirms it. Existing listeners are never reused. The current whole Home run passes.

The package witness initially used unsupported Python 3.9 (documented minimum 3.12), then the pack-only opt-in correctly failed the version-stamp guard. Matching explicit build/pack inputs pass with Python 3.14. Architecture subsequently rejected published-package API tracking on the nonpackable examples; removing that metadata and its own analyzer locks leaves the original guard unchanged and the whole suite passes 621/621.

## Delivery authorization and limits

The owner explicitly authorized resumption, additional review rounds within reason, and personal-account admin self-merge when CI is absent or too slow. Each merge records the exact reviewed head, CI state and use of that exception. Pending, cancelled, superseded and skipped Actions checks are not represented as passed. No release, deployment or remote package publication is part of this delivery. Acme examples are excluded from normal publishing; only the owned local witness opts into packing.
