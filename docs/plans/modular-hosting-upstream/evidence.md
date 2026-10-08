# Baselines and Evidence Register

Initial source/issue snapshot read on 2026-10-08; that baseline inspection ran no builds/tests. Later execution evidence is recorded below. Update each row from the exact revision before implementation or completion claims.

## Local CShells ownership checkpoint

CShells #144 is locally implemented on `016-share-host-singletons`, commit `9e66d41321083a456e132a4979c3ed997a367d9f`, clean worktree `/tmp/hosting-cshells-ownership`. The worker ran 11 ownership tests and built CShells for net8/net9/net10 with zero warnings/errors. Root reviewed the final source and ran service-registration, reload and ownership tests against the built Debug output: **32 passed, zero failed/skipped**. Root's first no-build invocation requested absent Release output and executed no tests; the corrected Debug invocation is the passing evidence.

The ownership mutation test failed as intended when instance descriptors were replaced with child factories: draining the old generation disposed the borrowed host object. The mutation was reverted, then the final implementation passed. Root-requested keyed independence, two named shells, caller disposal and null/generic diagnostics were incorporated. See the [owning issue checkpoint](https://github.com/valence-works/cshells/issues/144#issuecomment-6055941969).

Full CI, external current-head review, push/PR, merge, resulting-main checks, published preview and Foundation adoption remain outstanding. No milestone is complete. Git/session preference is pending before push/PR. Delegate model fallback was Luna High because Luna Extra High was unavailable.

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
