# Validation Guide: Studio Feature Impact Review

This guide records the validation sequence for implementation. No checks or host runs were performed while preparing the plan. Root owns shared-machine heavy gates and final integration.

## Source and evidence boundary

- Approved source base: `7fb745f8604b6ed67a2eae60266976c98bce949e`. The approved Spec996 commit is `1d8215409fc114351ef60a03f955bfefa65975c6`.
- Main later advanced to `73a4f8bf` in a separate Connections-only change. Root will reconcile this unit against current main in one serial integration lane before final source gates.
- Source-only observations are not runtime proof. Report only checks actually run, with their source revision, command, outcome, and safe evidence.
- Keep browser/network receipts to method, path, status, content type, response shape, and harmless feature IDs. Never retain headers, tokens, cookies, credentials, configuration setting values, or value-bearing failure artifacts.

## 1. Offline baseline and focused test-only RED

Use the locked Studio workspace and do not change package manifests or lockfiles:

```bash
cd Studio
pnpm install --offline --frozen-lockfile
pnpm --filter @elsa-workflows/studio-feature-management exec vitest run src/__tests__/module.test.ts
```

Record the actual baseline test count, files, exit status, and full output before adding tests; do not assume a previous count. If installation needs a pin/lock change or the untouched baseline fails, stop and report it rather than continuing.

Before any product edit, add the behavior regressions only in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts`. Reuse current feature fixtures and teardown helpers. Keep `module.tsx`, `styles.css`, SDK, backend, and manifests unchanged for this checkpoint. Mount authenticated cases through the existing public SDK auth provider and use its anonymous provider for demo coverage; do not recreate the SDK permission implementation in a test double.

The single focused RED run must exercise these independent contracts without asserting source text:

- Existing required single/bulk cascade is reflected in impact, optional edges are excluded, and filtered-out affected features remain visible.
- Multiple pending direct roots each show a cycle-safe transitive required-edge explanation path for an automatic change; reversing a root recomputes the remaining reasons, and a net-zero feature disappears.
- Superseded-root regression: from baseline `A=off`, `B=on` with required edge `A → B`, disable B and then enable A. Assert final `A=on`, `B=on`; B has no impact row; and `(B, off)` classifies or explains no surviving change. In a bounded companion case, leave a direct setting edit on B different from baseline; assert B is then present only for that direct setting change and the superseded `(B, off)` state root is still absent.
- Retained-effect regression: from baseline A=off/B=off with required A → B, enable A and then disable A. Preserve final A=off/B=on; show B as an automatic retained earlier-cascade effect, omit net-zero A and assert no direct B edit or pending/current A requirement. Reset clears both impact and retained provenance.
- Direct setting edits appear using existing sensitive-value handling; supplied restart metadata is distinguished from unknown reload behavior.
- Cancel sends no Apply and preserves the draft. Reset only returns the selected host to its loaded baseline.
- Apply after a current review uses the same host, complete state, and revision; a draft mutation, host switch, refresh, or catalog replacement invalidates it.
- Authenticated read-only users can inspect but cannot edit, bulk-edit, Reset, or Apply; manage users keep those controls; anonymous/auth-disabled demo sessions remain writable.
- A stale-revision 409 and a guard-refusal 409 retain their backend reasons and neither is presented as success or both described as stale.

Run the focused suite while the product module is still unchanged. Confirm that failures reach the expected new review/permission behaviors rather than setup, provider-mount, or unrelated baseline failures. Preserve the full command output for root review. Stop for root approval of this meaningful test-only RED before changing `module.tsx` or `styles.css`.

## 2. Focused GREEN and module checks

After root accepts the RED checkpoint, implement the planned module, test, and scoped style files. Remove the module's obsolete local SDK declaration shim as recorded in the plan so the existing public SDK types remain authoritative. Run the focused suite after each coherent behavior change, then the complete module suite:

```bash
cd Studio
pnpm --filter @elsa-workflows/studio-feature-management exec vitest run src/__tests__/module.test.ts
pnpm exec tsc --noEmit -p ../Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/tsconfig.json
pnpm --filter @elsa-workflows/studio-feature-management build
```

The tests must observe rendered behavior and the selected host's outgoing Apply call. Retain existing meaningful cascade, full-state payload, host isolation, conflict, filtering, and settings tests. Do not replace them with assertions on implementation text or a test-only copy of the product cascade.

## 3. Existing backend contract preservation

The feature is UI-only, but verify that the established Apply and configuration revision behavior remains intact. Use the normal dotnet build-slot wrapper and keep these runs serial:

```bash
dotnet test Essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj --filter 'FullyQualifiedName~FeatureManagementServiceTests|FullyQualifiedName~JsonShellFeatureConfigurationStoreTests'
```

This preserves full-state Apply, stale configuration revision rejection, current save/refresh/reload ordering, failure outcomes, and JSON-backed host configuration behavior. Do not change backend/API/SDK/auth contracts to make the UI pass. Root owns this gate if the shared machine is under load.

## 4. Actual Studio.Web and Workbench proof

Use the canonical [Spec993 composed Workbench/private-host guide](../993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison) for the guarded private root, copied normal Studio and Workbench configurations, fresh per-profile databases and data-protection keyrings, isolated Nuplane paths, standard Development UI sign-in, process launch, and cleanup. That guide is the source of setup and cleanup; this plan does not duplicate or replace it. Use a fresh private root and browser context for the before and after source revisions. Build both hosts from the exact source under qualification, serially, using the normal repository wrapper:

```bash
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj
dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
```

In the private normal composition, browse the supported Studio.Web `/settings/feature-management` surface (the legacy `/features` route may redirect there) and sign in with the source-provided Development identity without recording credential values. Keep the existing normal supplied modules. Do not enable optional providers, change global/user configuration, use a copied personal session, or add an identity/API fixture.

On the untouched baseline revision, record existing staging, single/bulk cascade, host selection, and Apply behavior for each target independently, along with the absence of a pre-Apply review. Do not attempt to open or cancel a review control that does not exist on baseline. If exercising the existing Apply path, use only a safe reversible already-loaded change under the same host-file boundary below; restore the private copy and clean up that guarded root before creating the fresh after-revision root and browser context.

On the post-implementation revision, exercise the new review acceptance for each target independently:

1. Record the names and safe structural state of the two private `shells.json` files without printing setting values.
2. Stage a single and bulk cascade that includes a feature outside the active filter. Confirm the review names the selected target and includes direct edits, automatic changes, all effective roots/paths, superseded-root/net-zero omission, optional-edge exclusion, and invalidation after a mutation.
3. Cancel and verify neither private `shells.json` changed.
4. Restage and review one reversible, already-loaded, non-provider change that does not disable Feature Management, authentication, or the API needed for the proof. Apply it and verify only the selected host's copied `shells.json` changed; verify the other host's file remains byte-for-byte unchanged. Do not use a new provider or a test-only module. If the live catalog offers no safely reversible choice, stop for root review rather than changing host composition.

The real normal-host Development seed proves the signed-in management path. Use a read-only identity in this browser proof only if the existing private seed already supplies one. Otherwise prove authenticated read-only rendering through the public SDK session provider and existing backend authorization tests; do not invent an Identity administrator or alter auth setup. A historical anonymous composition is not evidence for authenticated denial.

Afterward stop only the two processes started for this run, close the private browser, confirm owned ports are free, and use Spec993's guarded cleanup for the exact private root. Never mutate tracked host settings or shared configuration.

## 5. Cost-ordered workspace and repository gates

Root coordinates all heavy work serially; do not start these concurrently with other agents' builds. Run the focused module checks first. Then run the workspace checks from `Studio/`:

```bash
pnpm -r --workspace-concurrency=1 typecheck
pnpm lint
pnpm -r --workspace-concurrency=1 build
```

Run the repository architecture restore, architecture suite, and generated map check as required by the current repository guide:

```bash
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Keep the architecture restore and test in the prescribed order, and do not bypass build-slot queueing. A build or restore setup failure is reported as such, never as a passed gate.

Hosted CI retains full workspace test coverage; the local cost-ordered sequence runs the affected module suite and backend preservation suite rather than starting a recursive workspace test run.

Before delivery, root owns exact-head diff review and the independent source/review/CI axes. Merge only after green current-head gates. Because main moved beyond this unit's base, root then runs the actual resulting-main qualification and gates before reporting completion. No such check is run in this planning phase.

## Root-approved baseline reuse

For baseline observation only, root may reuse an owned normal composition after proving unchanged Feature Management module/styles and relevant host/API source against the approved baseline. Preserve the actual host build revision and unrelated differences rather than claiming an exact whole-application baseline. Use a fresh browser context and the staging/Reset/no-file-change observation above. Baseline Apply is optional; the new implementation's separate fresh-root Studio and Server reviewed Apply/correct-file proof remains required. See the bounded [research refinement](research.md#root-qa-refinement-reuse-source-proven-baseline-composition).
