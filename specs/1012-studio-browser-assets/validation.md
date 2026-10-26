# Validation history and local qualification

Local implementation and qualification are complete through source commit31834b62f5a82571409a97b0cd82b671cd3c34a7. Remote PR gates and resulting-main acceptance remain outstanding. The earlier stopped checkpoints below are retained as history, not current status.

## Earlier source and stop checkpoints

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

## Owner-authorized resumption

The owner explicitly replied "Resume #3361 with this exception" to the task-specific three-failed-check request. Fresh claim6428820628 resumes this leaf only; #3359 remains stopped. App/installation permission diagnosis was also corrected: needed Contents/Workflows grants exist, Git push dry-run succeeds, and the local Git wrapper now includes the already granted Workflows token scope. No settings/permissions were expanded. The original real-push403 cause is unproven; do not infer missing app access from the repository push:false field.

The retained failing trace proves the revision mismatch is a batching assumption: PUT(expectedRevision1) at18:53:25.283Z persisted the new nonnullable input without a default; PUT(expectedRevision2) at18:53:26.585Z persisted the nullable input with Literal/null default. They are1.302s apart, exceeding the existing800ms debounce. Both saves correctly advanced the server revision. The failure was expecting the second UI edit group to remain revision2. Trace/semantic receipts are retained locally at `/tmp/studio3361-revision-mismatch-trace.zip` and `/tmp/studio3361-revision-mismatch-requests.json`.

The scenario now explicitly awaits each intended save: created1, added input2, allowed null3, applied default4, Write line5, Delay6, reload6, conflict reports server7, recovery starts1. Exact revision assertions and final saved contract/graph/conflict/recovery assertions remain intact. No autosave/product implementation changed. Resumed typecheck, lint(0errors/56warnings) and build pass in order. The complete Chromium project passed406 cases with6skipped (412 total,5.4m; `/tmp/studio3361-resume-browser.log`).

The first actual production probe invocation packed/restored/published all profiles and verified the standard resource inventory and real Home heading. Its browser driver then failed because it requested a Home button; the actual shell renders a Home navigation link. The driver was corrected to the existing accessible link contract. This unsuccessful probe is retained at `/private/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/studio-hosting-assets-vbe4g8sa`; it is not counted as consumer acceptance. Corrected runs are recorded below.

## Production consumer and first review round

Corrected actual-production runs `studio-hosting-assets-y8hunaeh` and `studio-hosting-assets-g0d_ra7v` passed. Each standard/fallback profile verified140 packaged shell/module resources, exact HTTP bytes/MIME/no-cache/ETag304, SDK byte range, root/deep link, real API/asset404, candidate host/SDK/module versions, and actual Chromium Home with shared React/SDK/UI and no page errors/missing assets. The direct-reference frontend opt-out retained6 module resources and server discovery. Same-path overlay failed specifically at SDK asset collision. Owned producer/feed/consumer/cache were hidden before runtime; all consumer libraries were packages and frontend invocation count was0. Candidate source was b1770e400; later changes to pack checks and Web migration require final candidate qualification.

The missing-vendor run `studio-hosting-assets-i9gouevv` exited1 specifically at `HTTP asset mismatch: /studio/vendor/react.js (status 404)`; the subsequent normal run above passed. Producer guard controls in `studio-hosting-assets-vbe4g8sa/guard-control.json` proved missing SDK rejected, disabled guard packed an incomplete payload, and restored guard/SDK packed complete bytes.

Independent round1: standards found none; acceptance found the required app JS/CSS absent from the pack guard; correctness found stale ignored Web output would survive in existing checkouts. Both findings were verified and fixed. Missing application JS packed successfully before the fix; missing index.js and index.css now each fail the specific guard, and restoring both packs successfully (`guard-app-before-fix.json`, `guard-app-after-fix.json`). CI's asset verifier now covers26 entries and its self-test passes. Web excludes and removes only its obsolete generated wwwroot/studio subtree during build. An owned old checkout with a planted old index failed publish at static-asset collision before the fix; after the fix publish passed, removed the old subtree and retained Hosting's exact index (`studio-hosting-assets-y8hunaeh/web-migration-control.json`). Shared working-tree configuration and generated assets were not mutated by these controls.

The first architecture run passed589/590; its one failure located a stale SDK contribution link in the Runtime EXTENSION_POINTS.md. Correcting that active link produced590/590, and the maps check passed. The20-package whole Studio Release build/pack/inventory passed. A separately published actual reference Web also served all134 Hosting resources,14 selected modules, the real root/Home browser application and the runtime allowlist without a private management-key sentinel (`studio3361-web-kq16j7g4`). The initial Web smoke command used Consumer.dll by mistake; the corrected command used Elsa.Studio.Web.dll. This was a harness invocation failure, not a passed check or product startup defect.

## Final local qualification

Independent round2 reviewed the full base2fd983021891056b092e69914073cfb4c38862af → head31834b62f5a82571409a97b0cd82b671cd3c34a7 diff. Correctness, standards and acceptance each reported no actionable findings. No implementation was delegated.

After the project-file fixes:

- Whole Studio Release build passed; whole Studio xUnit project passed511/511,0skipped,80s (`studio3361-review-fixes.trx`). Discovery504 =503 direct cases plus one eight-row runtime theory.
- Both Release/Debug architecture graphs restored in locked mode. Whole architecture suite passed590/590,0skipped,32s. Discovery581 expands two runtime theories (six collectible-artifact rows and five capability-aggregation rows) by9 cases; no discovered case was omitted.
- Generated-maps check passed without committed filter changes.
- Full Studio Release pack/inventory passed with exactly20 expected packages at4.0.0-assets3361-candidate, correct license/icon metadata. No package was published remotely.
- Asset-verifier self-test and actual26-entry check passed. Frontend typecheck/lint/build,3642 workspace cases and406 Chromium cases/6skipped remain the completed affected frontend evidence; these later fixes change producer checks, Web migration and documentation, not browser application code.
- Actual production HostingAssets.Probe passed again from committed source31834b62f5a82571409a97b0cd82b671cd3c34a7: `studio-hosting-assets-nii19v_b`. Tool source and producer revision match; Python/browser driver hashes are recorded. Standard/fallback each140 resources, opt-out6, actual Chromium root/Home deep link and shared imports, package-only graph/isolated cache, hidden owned inputs, zero consumer frontend calls, all HTTP contracts and collision control passed.
- Actual published reference Web passed after the fixes: `studio3361-web-y1x5r44h`,134 Hosting resources,14 modules, real Chromium Home/root/deep link, one Hosting-owned payload, no private management-key sentinel in runtime/discovery. All owned processes stopped. Tests never renamed the shared checkout or altered its runtime configuration.

The source snapshot controls and browser reports live beneath `/private/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/` at the named artifact directories. Reproduce normal/negative/normal production-consumer qualification with the committed probe's README. Producer missing-output and obsolete-Web controls used owned snapshots and actual project files; no wrapper library substituted for production.

Exact-head GitHub CI, final PR review and merge/main/candidate acceptance remain delivery gates. Full backend/auth/custom-module/optional-addon combinations remain their separately scoped Program3340 consumer leaves; no release, deployment or publisher activation is included.
