# Tasks: Prebuilt Studio Browser Assets

Inputs: [spec](spec.md), [plan](plan.md), [research](research.md), [asset contract](contracts/browser-assets.md). Risk-based verification; no test-first cadence is mandated. One implementation worker/leaf; independent read-only review at delivery.

## Phase1 — Setup and ownership

- [x] T001 Verify #3361 prerequisites/claim/main health, record source-path inventory, allocate spec1012 and review spec/plan/contracts/checklist before implementation.
- [x] T002 Move Web Client, StudioSdk and StudioUi into Studio/src/essentials/Elsa.Studio.Hosting with git rename tracking; preserve source/runtime contracts and private npm identities.
- [x] T003 Update active workspace/lock/build/test/example/browser/tooling/Docker references and current contributor/canonical docs; preserve frozen historical consolidation manifests and completed spec evidence.
- [x] T004 Update shell/module CSS classification, harness/generated-output ignore rules and source-boundary checks; remove the obsolete Web generated shell payload and prohibit a second maintained copy.

## Phase2 — US1 packaged standard browser

Independent test: actual default package-only published consumer, full resource inventory and browser application/module route.

- [x] T005 Build frontend into Hosting's existing Razor static-web-asset owner; add producer-only required-output pack checks in Elsa.Studio.Hosting.csproj without consumer frontend hooks.
- [x] T006 Update tools/ci/verify-studio-assets.py and its self-test for the new owner, preserving full module inventory; qualify frozen install and whole frontend build/test/typecheck/lint paths after the move.
- [x] T007 Create tools/studio/HostingAssets.Probe using actual production projects/archives, owned committed producer snapshot, explicit source/candidate hashes and complete archive/resource accounting. Accept a supplied artifact directory for later CI integration.
- [x] T008 Implement external Hosting+Dashboard consumer with public composition and selected shell, isolated source mapping/cache, locked restore/publish, denied frontend shims and hidden owned producer/feed/consumer source/cache.
- [x] T009 Verify every shell/module resource's published and HTTP bytes/MIME/cache/range, index/deep link/runtime/discovery and real unknown API/asset404; Chromium boots the actual application and selected module route with no missing import dependencies.

## Phase3 — US2 shared module contracts

Independent test: existing module clients compile against moved SDK/UI source facades and load using the published import map.

- [x] T010 Qualify complete affected frontend workspaces and Studio .NET project after active source-mirror updates, compare discovery/execution counts, and verify actual candidate host/SDK/module compatibility versions and shared React/SDK imports.
- [x] T011 Verify no frontend source/tool inputs leak into actual NuGet/publish output, no Web/Nuplane dependency or source/project reference enters the consumer graph, and the reference Web uses Hosting's one payload. Keep the original wrapper spike explicitly historical/bounded and runnable from the new source owner.

## Phase4 — US3 customization and negative controls

Independent test: actual packaged server behavior and module resources remain with the direct-reference frontend opt-out.

- [x] T012 Extend the production probe for separately named fallback, rejected same-path overlay and direct Hosting ExcludeAssets="build;buildTransitive" custom frontend; verify server discovery and retained module assets after hiding inputs and denying frontend tools.
- [x] T013 Prove missing producer output is rejected specifically and a published missing vendor/SDK resource causes the expected HTTP/browser failure; restore exact inputs/bytes and complete the normal profiles again. Do not count controls or aborted attempts as green delivery checks.

## Phase5 — delivery qualification

- [x] T014 Run affected locked restore if references change, both architecture graphs/whole architecture suite/maps, complete Studio package inventory/full pack, actual Web smoke and required frontend/runtime checks; record precise scope and limitations in validation.md.
- [ ] T015 Open and attach one draft PR for #3361 map acceptance criteria to evidence, run independent correctness/standards/acceptance review to convergence, set the spec terminal status in the implementation PR, and obtain exact-head CI/formal review.
- [ ] T016 Verify actual reviewed merge/resulting main and candidate payload before closing #3361 or its parent; reconcile native Project63/Program3340 state and keep #3359/#3360 blockers separate.

## Dependencies and execution strategy

T001 → T002–004 → T005–006 → T007–009 → T010–011 → T012–013 → T014–016. Start with one moved source/build owner, then prove standard consumer and shared module contracts, then customization and delivery. Source inventories and independent review lenses may be read concurrently; implementation, mutation controls, commits and runtime lifecycle stay coordinated in one worker. No implementation delegation. No new GitHub issues are generated for this existing leaf.
