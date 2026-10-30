# Tasks: Linen Studio and host branding

## Setup and foundation

- [x] T001 Record accepted scope, research and contracts in specs/1171-linen-studio/.
- [x] T002 Add navigation/background roles and document additive contract in Studio/docs/design-tokens.md.

## US1 — Work in Linen

Independent check: fresh Studio opens in Linen; graph/sequence inspector and controls retain authoring behavior.

- [x] T003 [US1] Add Linen style and palette in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles.ts and foundationThemes.ts.
- [x] T004 [US1] Apply restrained shell/control treatment in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles.css.
- [x] T005 [US1] Apply modular designer/list treatment in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workspaceAppearance.css and graph-authoring/workspaceAppearance.css.

## US2 — Keep appearance choices

Independent check: all palettes/modes and explicit arrangement/preset choices survive reload; custom themes arriving later stay selected.

- [x] T006 [US2] Add missing/default versus explicit preset resolution in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance.ts and components/ThemeProvider.tsx.
- [x] T007 [US2] Extend behavior/rendered coverage in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/__tests__/workspaceAppearance.test.tsx and appearance.test.ts.
- [x] T008 [US2] Qualify palette modes and navigation contrast in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/__tests__/signatureThemes.test.tsx and Studio/tests/browser/workspace-appearance.spec.ts.

## US3 — Brand entry and workspace

Independent check: host configuration reaches shell/status and actual login; replacement receives security context while existing POST remains intact.

- [x] T009 [US3] Add explicit branding/default runtime configuration in Studio/src/essentials/Elsa.Studio.Hosting/StudioRuntimeScript.cs and Client/src/app/runtime.ts.
- [x] T010 [US3] Apply safe brand assets/name/links to Client/src/app/App.tsx and auth/studioAuth.tsx under Studio/src/essentials/Elsa.Studio.Hosting/.
- [x] T011 [US3] Add configurable/replacement login presentation to Essentials/src/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity/ and update its extension catalog.
- [x] T012 [US3] Bundle approved optimized login images under Essentials/src/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity/.
- [x] T013 [US3] Prove runtime configuration and login security/presentation behavior in Studio/tests/Elsa.Studio.Tests/StudioRuntimeScriptTests.cs and Essentials/tests/Foundation/Identity/Tests/AspNetCoreIdentity/LoginPageTests.cs.

## US4 — Integrate a branded host

Independent check: documented example uses public configuration/replacement seams and builds without product edits.

- [x] T014 [US4] Write Studio/docs/branding.md and a complete runnable branded-host example, linking existing hosting/theme/token guides.
- [ ] T015 [US4] Verify example consumer compilation and served asset integration using existing host/sample paths.

## Qualification and delivery

- [ ] T016 Run full affected suites, red/green bite proof and browser qualification; record evidence in specs/1171-linen-studio/quickstart.md.
- [ ] T017 Run architecture/maps, public API checks and review constitutional constraints.
- [ ] T018 Commit, open/attach draft PR and complete three independent review perspectives, fixing verified findings in new commits.
- [ ] T019 Qualify exact head against current main, ready/review/merge through org protocol and verify resulting main; mark specs/1171-linen-studio/spec.md Implemented.

Dependencies: T001 → T002–T008 (US1/US2); T009 → T010; T011 → T012–T013; US1–US3 → US4 → qualification. Distinct CSS, runtime and identity files permit parallel tooling but implementation remains sequential to preserve clear ownership. MVP is US1+US2; authorized delivery includes all stories.
