# Qualification

1. Build the affected Studio frontend and identity/hosting projects through normal tooling.
2. Run affected whole Vitest and .NET suites; demonstrate a failing-before/passing-after behavior assertion.
3. Open owned local Studio/identity hosts, inspect fresh Linen light/dark/dim/high contrast, all palettes, settings, workflow graph/sequence/BPMN and inspector. Edit/save/reload a workflow; keyboard and resize/readonly remain usable.
4. Exercise all login layouts at 320/768/desktop, error and keyboard states. Apply custom branding/images; prove callback replacement gets sanitized return URL and real antiforgery data.
5. Run documented branded-host example; architecture/maps and affected package consumer checks.
6. Record exact command/count/browser evidence on the PR, independent review rounds, exact-head required CI, and resulting-main status.

## Qualification receipts

The PR comments are authoritative for the reviewed/CI head and merge status. Initial receipts:
- Hosting frontend: 57 files / 672 cases passed after the host-default regression fix.
- Workflow frontend after integrating main's BPMN Decision work: 203 files / 2,762 cases passed.
- Identity: 532 cases passed, no skips; discovery lists 532.
- Studio .NET: 645 cases passed, no skips. Discovery lists 638 descriptors; dynamic theories expand at execution. Final TRX/count reconciliation is recorded on the PR.
- Architecture: 592 cases passed, no skips; generated solution-filter check passed.
- Browser: 94 appearance cases plus 24 actual-renderer consumer cases passed. Layouts covered split/reverse/immersive/editorial at 320/768/1440px in light and dark, including error/keyboard/contrast/overflow checks.
- Public presentation consumer compiled against the provider assembly and served all four bundled assets locally. Northstar sample published the standard Studio host in Release and ran against an owned Workbench backend. Real sign-in, custom name/logo, disabled resource links, workflow creation, activity insertion, input edit/save/reload and palette-mode switching worked; the reloaded input was `Hello from Linen`. BPMN root creation and inspector also rendered under Linen.
- Two independent review rounds fixed host-default persistence and workspace-role naming/fallbacks; round 2 returned clean in all three lenses. A final review covers designer-style deferral and these documentation receipts.
- Designer-only decoration was moved into its deferred stylesheet. Definitions landing measured 387.24 kB against the unchanged 387.50 kB budget; all heavy-surface exclusions remained in place.
- One local .NET validation attempt overlapped frontend output regeneration and failed on a missing generated asset; subsequent build/test qualification is sequential. It is not a product-test pass.

Additional initial diagnostic against baseline `7b4951365`: a strict direct host-tsconfig probe reported 103 existing diagnostics in both that base and candidate, with no added diagnostics. This was not repeated as a final gate. The repository's standard frontend typecheck is the supported gate and passes; this additional diagnostic probe is not described as green.


The work unit was renumbered from 1171 to 1172 before delivery because the earlier concurrent
Switch-authoring PR #3495 already uses 1171. The issue/branch/PR identities are unchanged.

The final whole-workspace frontend run passed 3,996 cases in 306 files; three package scripts contain
no test files and are not counted as passed tests. Studio TRX reconciliation covers all 638 discovered
descriptors with 645 executed/passed cases (runtime theory expansion accounts for the difference),
zero uncovered descriptors and zero skipped/aborted cases. Final architecture and map checks passed.


The callback seam also has mutation bite proof: temporarily ignoring configured LoginPageOptions in
the real GET endpoint made the replacement/security-context integration test fail (`Assert.NotNull`,
1 failed / 0 passed). Restoring the endpoint passed the full Identity suite, 532/532 with zero skips.
The final browser rerun after designer-style deferral passed all 118 cases again. Temporary browser
qualification files live outside the committed tree; canonical appearance tests remain in Studio/tests/browser.

Supported lint/typecheck, all-workspace frontend tests, production frontend builds/budgets, whole
Studio/Identity/architecture suites, map checks and the branded published host are the local gate set.
Required exact-head CI and merge/resulting-main results are recorded on PR #3498; local passes do not
replace pending CI. Verify descendant main after merge and post its check state before reporting delivery.
