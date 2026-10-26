# Partial validation and stopped checkpoint

This is not delivery evidence. Spec1012/#3361 remains incomplete; no PR/required CI/main acceptance is claimed. The Code Review/package-consumer steps have not run.

## Implemented source ownership

Client, StudioSdk and StudioUi moved from Web to Hosting (196 source files with rename tracking). Private npm identities and frontend contracts remain unchanged. Active workspace/lock, module-test/browser/example, stylelint/layout, Docker, npm-package workflow and asset-verifier paths now use Hosting. Historical consolidation maps, incident build attestations, nested historical workflows and completed specs are preserved. Web's134 obsolete generated shell files were removed; new generated output is Hosting-owned and ignored. Default .NET content excludes all three frontend source/tool trees.

A producer-only GenerateNuspec guard checks index, SDK and six import-map vendors. Initial actual Hosting pack at4.0.0-assets3361-baseline succeeded. Its134 staticwebassets entries match generated source bytes exactly, with zero Client/StudioSdk/StudioUi/node_modules source leaks. Missing-output guard controls and full dependency/published-consumer acceptance are outstanding.

The production HostingAssets.Probe is drafted (Python and Node syntax checks passed), with actual-project source snapshot/archives, isolated consumers, customization and browser plans. It has NOT been executed and is not qualified. No wrapper spike is presented as production proof.

## Checks completed before the stop

- `pnpm install --frozen-lockfile`: passed using11.9.0. Lock changes are31 importer/link path substitutions, with no resolution changes.
- `pnpm typecheck`, `pnpm lint`, `pnpm build`: passed in order after the move. Lint0errors/56existingwarnings; layout22projects/22packages/22module roots.
- `pnpm --recursive --workspace-concurrency=1 test`: whole workspace passed3642 cases (19 nonempty suite summaries; pass-with-no-tests workspaces also completed).
- Whole Studio xUnit project511/511,0skipped (`studio3361-source-move.trx`,1m22s). Discovery504 accounts for503 direct entries plus the existing eight-row ModuleManifestProvider theory.
- Producer asset verifier self-test passed, actual24asset inventory passed, NuGet inventory self-test passed.

These frontend type/lint/build checks precede the later two browser-fixture animation-wait edits; repeat applicable checks on resumption. No new whole-project rerun is claimed after those edits.

## Browser attempts and diagnosis

1. Whole Chromium fixture project:402passed/4failed/6skipped of412 cases (5.2m). One compensation dialog and three light incident dialogs failed axe contrast during entrance animations. Production Hosting and Workflows CSS are byte-identical to main. An owned real-browser diagnostic reproduced color-contrast failures while two finite120/130ms animations were running; the same DOM passed after their completion. The two existing seriousViolations helpers now await finite running animations before axe, preserving every assertion and normal-motion behavior. No product CSS/color changed.
2. The first restart aborted before executing tests because the owned diagnostic Vite server still held4179. Cleanup initially expected an absolute command path, but Vite used a relative one. Its exact cwd/command were verified, only that owned process received SIGTERM, and the port cleared. This is an unsuccessful check attempt, not test execution or a pass.
3. Whole corrected Chromium project:405passed/1failed/6skipped of412 cases (5.2m). All earlier accessibility failures pass. `activity-definitions.spec.ts:678` (create, graph autosave, reload, conflict preservation/recovery) failed atline702: saved revision expected2, observed3 for15s after the input/default edits. The fixture assumes a fixed number of debounced saves. That is an observed mismatch, not yet a proved fixture-only defect or qualified product fix. No changes to that scenario or further checks have run.

The org AGENTS stop condition applies: the same whole browser check failed three consecutive attempts, including the pre-test port conflict. The #3356 exception is specific to3356 and does not cover this leaf. Work stops here pending an explicit owner exception/requeue. Preserve the branch and inspect the saved trace/error context on resumption. Do not weaken assertions or call the browser project green.

Remote delivery is separately blocked by the previously reported app-authenticated Git push403. No new push attempt, alternate identity, permission bypass, release, deployment or publisher activation occurred. #3359 remains stopped; #3360 remains on its independently reviewed saved branch.
