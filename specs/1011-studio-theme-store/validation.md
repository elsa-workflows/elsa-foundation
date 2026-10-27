# In-progress validation

This is a partial #3360 checkpoint, not delivery/completion evidence.

The optional plain SDK package compiles with zero warnings/errors. Its public options/composition/features are tracked; endpoints/models/directory reader/writer remain internal. It references Hosting/Api/Core and ASP.NET Core, with no Web/Nuplane dependency.

Whole Studio project: **528 passed, 0 failed, 0 skipped**, `studio3360-composition-fixed.trx` (1m08s). The original 511 cases remain, plus17 public-API composition cases covering independent features/core dependencies, optional absence, host authorization, alternate ContentRoot-relative paths, callback precedence and retained-object mutation, typed startup validation and duplicate/after-Build guards.

The first run passed521 and failed7 invalid-startup cases because fixture cleanup called Host.StopAsync before startup constructed its hosted-service list, masking the expected options validation exception. The fixture now stops only successfully started hosts; all7 pass in the full rerun. Successful fixture hosts stop before disposal and unused console logging is cleared.

Web still uses its existing theme code. The copied addon implementation still requires managed/read projection separation, source-removal/shadow preservation tests, final asset hardening, Web adoption/obsolete removal and existing fixture migration. Frontend/runtime, causal controls, inventory/locks/package/CI/reviews and actual resulting-main verification remain open. No PR/push/release has occurred. #3359 stays stopped and unchanged at its saved checkpoint on its separate branch.

## Managed persistence checkpoint

The private managed model/read snapshot separation is implemented in the addon. Each theme/default/visibility operation writes only managed state; directory enrichment remains in reads. Explicit source-owned deletion is refused instead of falsely reporting removal. Existing shadowed managed definitions and stored defaults excluded by the source mode are preserved.

Whole Studio project **539 passed, 0 failed/skipped**, `studio3360-managed-state.trx` (1m12s). Eleven additional public-host cases cover all three source modes, directory removal, complete shadowed-state preservation, intended save/import/duplicate/default/delete changes, deliberate adoption through an explicit save, source-owned refusal and preservation of a projected-out default.

Causal control: reconstructing managed state from the merged view in the visibility writer reproduces the old defect. Whole-project negative check: **536 passed / exactly3 failed**, the no-copy regression under BuiltInsOnly, DirectoryOnly and AddToBuiltIns. The source was restored byte-for-byte; whole-project restored check **539 passed, 0 failed/skipped**, `studio3360-persistence-restored.trx` (1m07s). This proves the regression tests detect the removed behavior. No unrelated source was changed by the control.

Concurrency/assets/cancellation qualification, migration of old Web fixtures and Web adoption, frontend/runtime changes and delivery gates remain outstanding.

## Web adoption checkpoint

Web now references the optional package and calls AddElsaStudioThemes. Its copied theme API/models, three feature classes, app-only configuration type and directory service are removed. The theme addon owns its assembly registration; the Web roster keeps distribution modules. The private Web ConfigFileWriter remains because module/feature administration still consumes it until #3359; the theme addon uses its own permitted private atomic writer.

Existing theme tests now use public composition plus test-owned JSON records, with host recreation for installed option changes. The previous directory-copy expectation is replaced by preservation of the original managed definition. Invalid modes fail through normal options startup validation. No production friend was added.

Whole Studio project **539 passed, 0 failed/skipped**, `studio3360-web-adoption-fixed.trx` (1m07s). The first adoption run passed537 and failed two discovery fixtures that still constructed only the old host roster. They now compose the public theme addon exactly as Program does; the whole rerun passes.

## Asset, concurrency, browser and package qualification

Current complete Studio execution: **556 passed, 0 failed, 0 skipped**, `studio3360-typed-source-mode.trx` (1m16s). `--list-tests` lists549 entries:548 execute directly; `StudioModuleManifestProviderTests.GetModules_ProjectsModuleManifest` expands from one discovery entry to eight runtime data rows. Comparing result names accounts for the seven-case difference; no discovered case was omitted. The public options now use the canonical `StudioThemeSourceMode` enum, while legacy named configuration strings, trimming/casing and invalid blank/numeric/unknown rejection remain covered.

Material tests cover PNG/WebP/JPEG round trips, alternate ContentRoot-relative paths, nonempty/2MiB bounds, invalid IDs/unrelated files, cancelled partial staging cleanup and an actual paused response overlapping deletion. Concurrent distinct theme/visibility writes preserve all committed edits and valid JSON. The private JSON reader permits owned atomic pathname replacement while retaining its opened document. Uploaded files are staged privately and published only after complete copy and cancellation checks.

Causal controls ran against the whole Studio project and restored exact source bytes:

| Removed behavior | Expected negative result | Restored result |
| --- | --- | --- |
| Managed-only visibility persistence | 3 no-copy failures /536 pass | 539/539 |
| Strict material ID/inventory filter | 6 failures /549 pass | 555/555 after all three asset controls |
| Theme endpoint authorization requirements | 1 authorization failure /554 pass | 555/555 |
| Whole save/visibility mutation leases | 1 lost-update failure /554 pass | 555/555 |
| Startup materialization of copied settings | 1 pre-first-request configuration-edit failure /555 pass | Final complete run556/556 |

The first options negative control exposed a non-idempotent fixture callback, rather than proving the intended guard. The callback now seeds its source file only when absent, and a separate configuration-edit case proves startup copying causally. The immediate restored full run then stalled in the existing process-global ConsoleStream stress test: method-only stack evidence showed ConsoleStreamTee/SyncTextWriter/ConsoleLoggerProcessor contention. Its owned testhost was terminated; the aborted partial553-case result is not a pass. Clearing the remaining unused default console provider in a composition fixture and running the final normal suite produced the complete556/556 result above. No ConsoleStream production implementation changed.

Frontend application suite: **579/579** across51 files. Restoring the old Appearance page causes exactly the empty-inventory case to fail (578 pass); byte restoration returns579/579. The Theme Builder deletion journey also passes against the real page/provider, retaining a real management-inventory selection when the selectable default is empty. Browser Chromium: **5/5**, covering all three source policies, empty Appearance and delete/reselection. These browser journeys use actual components and SDK clients with mocked HTTP transport; they do not substitute for the isolated consumer journeys on #3364. `pnpm typecheck`, `pnpm lint` (0 errors,56 existing warnings) and `pnpm build` passed in order. Initial browser failure was an ambiguous Colour mode locator; scoping it to the Appearance region fixed the fixture.

`dotnet restore Elsa.Server.slnx --locked-mode` passed. Both CI graph configurations restored with `tools/architecture/restore-ci-project-graph.sh`; whole Architecture project **590/590**. Generated-maps `-- check` passed without solution-filter drift. Existing root restore/compiler/analyzer warnings remain visible in logs. Studio historically has no committed package lock files; none are invented by this extraction.

Actual candidate packages Core/Api/Hosting/Themes at local version `4.0.0-theme3360-probe` were packed. An external Web SDK consumer has only one PackageReference, `Elsa.Studio.Hosting.Themes`, and uses public Add/Use/Map APIs with nondefault Authoring shell and source/store/material paths. With an isolated NuGet cache and explicit candidate source mapping, all four libraries restore as packages; Themes' nuspec and the complete restored graph contain no Web/Nuplane dependency. Published DLL hashes match the archives. After hiding producer feed, consumer sources and restore cache, the published host starts, resolves the theme assembly from its own output, serves independently selected management/core capabilities, preserves empty DirectoryOnly projection and writes managed visibility to the configured file. The first harness attempt compared macOS `/var` and `/private/var` aliases literally; resolving both paths corrected that assertion. The corrected complete probe passed. No browser payload is claimed from Hosting yet; #3361 owns it and #3364 owns final consumer combinations.

Actual reference Web ran from an owned temporary working directory and ContentRoot with isolated Nuplane paths, empty package feed and unreachable backend. Discovery returned14 modules, registry/runtime responses succeeded without the management-key sentinel, all three selected theme capabilities returned200, and a management write used the configured theme store while preserving DirectoryOnly projection. It ran the real Program and addon, without a test-only host wrapper. No publisher, release or deployment was activated.

Remaining: draft PR review, exact-head CI/formal review, actual reviewed merge/main audit and native board completion. #3359 remains stopped on its saved branch.

## Independent review round1 and corrections

Independent correctness, repo-standards and acceptance reviewers inspected base `2fd983021891056b092e69914073cfb4c38862af` through `5afc5cdba4b1c8341fb5152865bd0206f9021e5f`. Three unique actionable findings survived verification:

1. Missing Themes identity in the CI inventory. The existing read-only inventory verifier reproduced the unexpected-project failure. The identity is now registered as introduced after consolidation; verifier self-test passes.
2. A source directory containing the managed store imported it as an external collection. Public HTTP regressions for source `.`/default store and source `state`/`state/themes.json` both failed before the fix. Directory enumeration now excludes the configured managed store, preserving these otherwise useful layouts. DirectoryOnly cannot select/default the managed-only theme, and management deletion succeeds.
3. Equal StoreFile/AssetsDirectory choices, or an asset directory below the future file path, passed startup but could not both work. Both invalid-option cases failed before the fix; startup now rejects them.

Combined whole-project negative check: exactly4 failures /556 passed (`studio3360-storage-negative.trx`), all four new cases. Corrected whole-project check: **560 passed, 0 failed/skipped** (`studio3360-storage-fixed.trx`). Full Studio solution pack at local version `4.0.0-theme3360-review` succeeded; the production verifier accepted exactly21 expected NuGet IDs, version, MIT license and icon. The isolated Themes-only consumer restored/published/ran the corrected candidate packages with hidden feed/source/cache and matching archive/output DLL hashes. Earlier556-case evidence remains historical; the current suite has four additional cases. Frontend source is unchanged by these review fixes.

GitHub delivery impediment: two app-authenticated pushes returned403 despite a contents:write installation token; fetch and issue comments work, and the repository reports push:false. Owner notified; no alternate identity or permission bypass attempted. Source/review fixes remain local until push access is restored. No PR or CI completion is claimed.

## Independent review round2 and filesystem case aliases

All three independent reviewers examined base2fd983021891056b092e69914073cfb4c38862af →4935ef192c0ddc196129228fa7910f0d668741c3 and agreed the remaining source exclusion compared case-sensitively on macOS, where this workspace's volume is case-insensitive. The same assumption affected future file/directory collision validation. This finding was retained; no findings were discarded.

Three public-host regressions cover directory and filename aliases plus nonexistent case-variant file/directory choices. On the real current filesystem they failed before correction: **3 failed /560 passed**, `studio3360-case-alias-negative.trx`. Case-sensitive volumes exercise ordinary no-copy behavior and permit genuinely distinct future paths. The private helper now queries macOS `_PC_CASE_SENSITIVE` through pathconf on the closest existing path, rather than assuming every Mac volume ignores case. The installed SDK sys/unistd.h defines the constant11; a local native query returns0 on this temporary volume. Windows preserves its case-insensitive comparison and other platforms ordinal comparison; the native call is guarded to macOS. Query errors become normal options validation errors or the existing directory-read fallback, rather than guessing.

Corrected whole Studio project: **563 passed, 0 failed/skipped**, `studio3360-case-alias-fixed.trx` (1m22s). Discovery556 entries accounts for555 direct cases plus the existing eight-row runtime theory. This supersedes the earlier560-case checkpoint. Changed source remains local pending round3 and resolution of the GitHub push403.

## Independent review round3 and physical aliases

Correctness and standards found no remaining actionable issues on03bf4b1f6178e95ed0197946370cbc330104f492. Acceptance retained one physical-alias finding: macOS `/var` and `/private/var` (and configured directory/file links) can address the same store while lexical paths differ. Four additional public-host regressions reproduce directory-link and file-link source ingestion plus future source/store and asset/store collisions through a linked parent. Whole-project negative result: **4 failed /563 passed**, `studio3360-physical-alias-negative.trx`.

The private helper now resolves existing directory and final file links through the public FileSystemInfo APIs before copying installed paths and comparing enumerated entries. Existing source/store aliases are rejected; a containing source directory skips entries resolving to the managed store. Future path suffixes remain supported. These link regressions exercise real links on Unix; Windows exercises ordinary same-path behavior without requiring link-creation privilege. No new public surface or production friend is introduced. Earlier candidate artifacts predate this final correction and will be requalified; no final delivery is claimed from them.

Corrected whole Studio project: **567 passed, 0 failed/skipped**, `studio3360-physical-alias-fixed.trx` (1m16s). Discovery560 entries accounts for559 direct cases plus the existing eight-row runtime theory. The current source is ready for independent round4; GitHub push access remains unresolved.


## Resumed integration with delivered Hosting and administration

The Git delivery blocker is resolved; its earlier permissions inference remains withdrawn. #3361 and #3359 are now delivered through separately approved PRs #3385/#3386. Their verified main baseline is08712740d242d7711f098e3d765dab5fc2cbcbcf. Its root/Maps/full-tier, Studio and CodeQL checks passed. Main native-hosting initially reported13 pass/1 known-quarantined failure (#3259); its configured retry passed2/2. The aggregate is green, with both attempts retained on #3359. This is not a clean-first-attempt claim.

The saved Theme branch integrates that actual baseline. Resolutions preserve both optional packages, Hosting-owned browser source/assets, the root-tracked module manifest provider, public Nuplane composition and the early/root Console Stream lifetime. The new selection test moved into Hosting/Client; browser fixture imports follow that owner. The package inventory contains22 identities. With administration already moved, the unused Web ConfigFileWriter is removed; Web now has only Program.cs and its distribution assembly roster as C# source.

Integrated source checks: Studio build0 warnings/errors; complete Studio tests626/626,0 skipped; discovery619 entries comprises618 direct cases and the existing eight-row module-manifest theory. Application Vitest590/590 across52 files; configured typecheck passed; lint passed with56 existing warnings/0 errors; configured build passed; five actual theme-page Chromium journeys passed. The first lint attempt overlapped Playwright recreating the generated test-results directory and hit ENOENT during scanning; rerunning after browser completion passed. This orchestration failure is retained rather than reported as a source defect or a first-attempt pass.

Locked CI graph restore, complete Architecture590/590 and generated-maps check passed. All14 Theme-package files and both changed appearance/builder pages are byte-identical to the prior reviewed d3e43b60 checkpoint (pages at their new Hosting paths). Therefore the earlier independent causal guard/appearance controls still bind the same production bytes; integrated whole-project/runtime checks above are new. Hashes/counts are in /tmp/studio3360-integration-provenance.json; command logs use /tmp/studio3360-integrated-*.

Fresh integrated package/consumer/reference-Web qualification, remaining independent review rounds, PR/exact-head CI, approved merge and resulting-main verification remain pending. Prior candidate artifacts do not qualify this integration. No release, deployment or publisher activation was performed.


## Integrated candidate qualification at a9a0cadbd

A fresh owned git-archive producer from a9a0cadbd517f7fb9ec8c62e05f3bff217dee79c built Release with zero warnings/errors. Its first default-parallel Release suite stalled in the existing process-global ConsoleStream long-poll stress harness. Method-only stacks (/tmp/studio3360-integrated-release-stacks.log) show ConsoleLoggerProcessor/ConsoleStreamTeeWriter/SyncTextWriter monitor contention and a storm request blocked in Console.WriteLine. The owned testhost was terminated; the aborted TRX reports 622 completed passes and is not accepted as a full-suite pass. No production ConsoleStream change or root-cause fix is claimed.

The entire same 626-case Release project then completed with collection parallelization disabled via an owned xUnit RunSettings file: 626 passed, zero failed/skipped, 3m04s. This is a diagnostic serial profile, distinct from the earlier ordinary Debug 626/626 pass. The final remote CI must run its ordinary unmodified suite. The first aborted report/log/TRX and the serial report/log/TRX are both retained.

The same producer packed exactly 22 actual Studio NuGet identities at local version4.0.0-theme3360-integrated. The production verifier's self-test and exact version/identity/MIT/icon validation passed. A fresh external Web SDK consumer has just one PackageReference, Elsa.Studio.Hosting.Themes. Its isolated locked restore contains 10 package-only libraries; no Web, Nuplane, Modularity, backend identity or cluster closure. Archive and published DLL hashes match. With its owned producer, complete package feed, selected consumer feed, restore cache and consumer source renamed unavailable, the published host passed independent management/core presence, picker/Nuplane absence, DirectoryOnly empty projection, configured visibility persistence, PNG upload/read/delete in its configured material directory, and prebuilt index/runtime responses. Denied node/npm/pnpm/npx invocation count is zero.

The actual reference Web app published from that same archive using the current public composition for both addons. HTTP checks matched all 134 canonical shell resources, discovered 14 modules/20 features, exercised the administration registry and all three theme capabilities, and persisted visibility under the configured theme store while preserving DirectoryOnly. Real Chromium then loaded the root, deep link and actual Home module with shared React/SDK/UI, no missing resources and no errors, while the actual Theme API confirmed DirectoryOnly. Background reconciliation/watch work was disabled only in this owned smoke profile.

Artifacts: /private/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/studio3360-candidate-sdbs1l2s. Reports: report.json (with initial abort retained), first-producer-attempt.json, theme-consumer-report.json, web-smoke-report.json and web-browser-report.json. Owned server processes stopped. Source a9a0cadbd includes the integrated production behavior; subsequent lifecycle/evidence metadata is not a new production candidate. No release/deployment/manual publisher activation occurred.

Local qualification is complete within the profiles above. Independent review resumes at round5 after the four saved Theme rounds; the six-round cap remains. PR/exact-head CI, formal review/merge and resulting-main acceptance remain pending, and Verification remains Partial.
