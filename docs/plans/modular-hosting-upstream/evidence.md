# Baselines and Evidence Register

Initial source/issue snapshot read on 2026-10-08; that baseline inspection ran no builds/tests. Later execution evidence is recorded below. Update each row from the exact revision before implementation or completion claims.

## Local CShells ownership checkpoint

CShells #144 is locally implemented on `016-share-host-singletons`, commit `9e66d41321083a456e132a4979c3ed997a367d9f`, clean worktree `/tmp/hosting-cshells-ownership`. The worker ran 11 ownership tests and built CShells for net8/net9/net10 with zero warnings/errors. Root reviewed the final source and ran service-registration, reload and ownership tests against the built Debug output: **32 passed, zero failed/skipped**. Root's first no-build invocation requested absent Release output and executed no tests; the corrected Debug invocation is the passing evidence.

The ownership mutation test failed as intended when instance descriptors were replaced with child factories: draining the old generation disposed the borrowed host object. The mutation was reverted, then the final implementation passed. Root-requested keyed independence, two named shells, caller disposal and null/generic diagnostics were incorporated. See the [owning issue checkpoint](https://github.com/valence-works/cshells/issues/144#issuecomment-6055941969).

Full CI, external current-head review, push/PR, merge, resulting-main checks, published preview and Foundation adoption remain outstanding. No milestone is complete. Git/session preference is pending before push/PR. Delegate model fallback was Luna High because Luna Extra High was unavailable.

Root subsequently ran the complete CShells.Tests project at unchanged `9e66d41321083a456e132a4979c3ed997a367d9f`: **666 passed, zero failed/skipped**, Debug/net10, six seconds reported by the runner. This is local regression evidence, not remote CI or a performance claim. The [issue records the run](https://github.com/valence-works/cshells/issues/144#issuecomment-6056073967).

## Local catalog checkpoint and safety research

[CShells #146](https://github.com/valence-works/cshells/issues/146) is locally implemented and root-reviewed on `017-runtime-catalog-commits`, commit `3318f5b1564a033bc5650641231740541fad80a3`, clean worktree `/tmp/hosting-cshells-catalog`. The stock resolved accessor exposes an optional commit source, queues exact snapshots with commits and dispatches serially in generation order outside refresh/queue locks. Concurrent/reentrant refresh can return while an earlier subscriber runs. Subscriber/logger exceptions cannot undo commits or prevent later delivery. No mandatory catalog member or independent mismatched DI alias is introduced.

Worker validation passed 20 focused catalog/accessor tests and net8/net9/net10 library builds with zero warnings/errors. A mutation that propagated a subscriber failure caused the isolation test to fail; the mutation was restored and focused tests passed. Root reviewed the final implementation/docs and requested bounded genuinely concurrent handoff tests, precommit cancellation fencing, assertions outside isolated callbacks and custom accessor compatibility. Root then rebuilt and ran the complete CShells.Tests project at final commit: **664 passed, zero failed/skipped**, Debug/net10. See the [local review checkpoint](https://github.com/valence-works/cshells/issues/146#issuecomment-6056337926). External CI/review/publication and consumer proof remain pending.

Spec Kit's branch-number discovery missed the `+` marker for a branch checked out in a sibling worktree and generated a second 016 prefix. The worker corrected the branch/directory to 017 without rerunning creation. This tooling finding is separate from production behavior.

Root combined the two verified branches locally in `/tmp/hosting-cshells-generation`, integration commit `352a25e`, without conflicts; this provides the base for [CShells #147](https://github.com/valence-works/cshells/issues/147), claimed as the only active implementation leaf under Feature #145. The approved contract reserves immutable attempted identity, begins after valid composition before catalog reads, and keeps protection through confirmed provider teardown. Framework root-owned retention preserves unresolved leases on cleanup failure independently of Shell reachability. No combined test result is inferred from the separate runs.

Nuplane #108's bounded non-destructive [store-use safety spike](../../reports/modular-hosting-store-safety-spike.md) confirmed same-process and sibling-process exclusive-open denial, then acquisition after disposal/owner crash on local macOS/.NET 10. It did not test the complete proposed lease protocol or delete package files. Loading inside an already-held reconciliation lock requires a scoped ownership handoff; prune must probe long-lived lease sentinels without waiting under the global lock. Exact paths, active/LKG closure, actual load-context death and a ratified shared-root upgrade/admission boundary remain destructive-readiness requirements. The owner choice for that compatibility boundary is pending. No report-only substitution closes #108 or unblocks final qualification.

## M2 draft extraction designs

Read-only research for [optional Nuplane integration](../../reports/modular-hosting-integration-candidate.md) and [startup activation/retry support](../../reports/modular-hosting-startup-candidate.md) maps current host behavior and test targets. These are proposals, not implementation-ready tasks or passed runtime proof. Foundation.Host disables both refresh/reload and acts on every enabled reconcile; Workbench refreshes changed/pending work even with auto-reload off. Both currently skip before active-shell detection, and Workbench can forget a change after a failed first activation initialized a stale catalog. Preserve explicit host policies and decide that recovery case in the adapter task rather than accidentally changing it.

The published Nuplane `.94` nuspec points to `bf27be646d4c124b6b2ba2c632f9a49b1a5252c6`. GitHub's base `bf27be6` → head `21e2c24` comparison shows the source snapshot is four commits ahead; among the relevant abstraction/observer/autoload files only `ResolvedPackage.cs` changed, adding `PackageContentHash`. Observer/catalog contract files are unchanged in that comparison. Implementation must still restore/build against the exact published package; source snapshots and package provenance remain distinct evidence.

## Source baselines

| Repository | Ref / commit | Evidence |
|---|---|---|
| `valence-works/cshells` | `main` at `49c912633d968197ddd430c5bd826acd9cfcb15d` | Remote contract snapshot was inspected; no upstream `ShareWithShells` API was present. The proposed native host ownership and catalog event are new work, not an existing API. |
| `valence-works/nuplane` | `21e2c24fe8070a92ded13f7cc391353f7c6df856` | Baseline carried by the program bootstrap note; refresh before any issue claim because #2362/#2363 may advance the branch and package pin. |
| `elsa-workflows/elsa-foundation` | `origin/main` at `8a2a97d4e31857779da547797eed10f478dd8874` | Descendant of base `e4a699879791fb7eb2bc1c6874f772f4a7fe5355`; worktree clean at read. |

The lead refreshed Foundation before creating `codex/2500-modular-hosting-program`. CShells task #144 is claimed in `/tmp/hosting-cshells-ownership`; Spec Kit selected `016-share-host-singletons`. These are execution state, not completed implementation evidence. Markdown local links and whitespace were checked for the program artifacts. Foundation [#2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remains an open main-CI incident/investigation; inspect exact current runs before attributing any failure or declaring resulting-main success.

## Current Foundation behavior observed

- Foundation `ShellServiceSharingExtensions.ShareWithShells<T>` remains Elsa-owned in `src/essentials/Cluster/Readability/ShellServiceSharingExtensions.cs`; it uses `HostContainer` to capture the root through CShells lifecycle subscriber ordering. It supports a narrow non-disposable singleton case and cannot define upstream semantics alone. `ShellServiceSharingTests` and `HostOwnedServicesAreSharedWithShellsTests` are the direct regression targets.
- `NuplanePackageGenerations` joins Nuplane's replaced assembly catalog with CShells feature snapshots and shell disposal/drain lifetime, then publishes Elsa schema-readability updates. It polls CShells snapshot generation because no committed-refresh notification is exposed. Keep that Elsa report sink local; upstream only the generic ownership/catalog/lifetime seam.
- Since base `e4a6998`, Foundation.Host `Program.cs` has added configured Data Protection and `AddShellStartupValidation`; preserve them in any host-library or composition integration. The current main diff also adds Workbench shell startup validation.
- Current Workbench `src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs` refreshes after a changed Nuplane reconcile, only when a shell is active. Auto-reload is enabled only when `Elsa:Shells:ReloadOnPackageChange` parses true; `src/apps/Elsa.Workbench/appsettings.json` sets it to `false`. The observer retains a failed change for retry and treats shell refusals as unpromoted generations. Foundation issue #2314 already asks to consolidate this observer and the duplicate assembly provider.
- Existing source tests for Foundation eager activation/retry/readiness, Workbench package refresh, service sharing, package generation retirement, and Nuplane/EF reconcile journeys define the consumer-side baselines. Refer to the target list in the PRD/roadmap; re-run only after implementation.

## Existing issue ownership and overlap

- [Foundation #2314](https://github.com/elsa-workflows/elsa-foundation/issues/2314), open/needs-triage: shared package-change observer/provider adoption in `Elsa.Modularity.Nuplane`; per-host reload defaults; one tests home. Adopt its scope.
- [Foundation #2362](https://github.com/elsa-workflows/elsa-foundation/issues/2362), open under program #2262: Nuplane collectible load contexts and Elsa retire hooks. Do not implement duplicate unloading.
- [Foundation #2354](https://github.com/elsa-workflows/elsa-foundation/issues/2354), open and assigned: extract Foundation.Host composition to a packable host library. Coordinate any edits to Program.cs/composition.
- [Foundation #2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164), open: upstream CShells build-start/catalog-generation evidence to close the pre-initializer readability window.
- [Foundation #2202](https://github.com/elsa-workflows/elsa-foundation/issues/2202), #2159, and #2331 are closed. They cover Foundation activation retry/readiness, root-vs-shell Nuplane reconcile identity, and shell `ValidateOnStart` execution. Their current behavior remains a regression constraint.
- [Foundation #2258](https://github.com/elsa-workflows/elsa-foundation/issues/2258) remains open for per-package hot-reload/restart policy. Shared observer work must respect package policies rather than treating every reconcile as permission to reload.

## Validation and publication constraints

- Foundation manages package versions centrally in `Directory.Packages.props`; locked restore requires reviewed `packages.lock.json` changes. Follow `docs/reference/nuget-lock-files.md`: plain solution restore after reference/pin changes, review affected locks, then refresh maps. Nuplane runtime packages also use Nuplane's own store lock; project restore does not update it.
- Foundation's Packages workflow plans, builds, packs, and publishes affected `src` packages from `main`; branch runs produce artifacts and do not publish. Preview publication must use that workflow and record the exact identity. Stable tags and production deployment are outside scope.
- Exact validation targets and completion gates are listed in `roadmap.md`. No check is marked green by this evidence register.
