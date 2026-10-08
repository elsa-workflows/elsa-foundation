# Optional CShells-Nuplane Integration Candidate

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md); [CShells #142](https://github.com/valence-works/cshells/issues/142). Status: draft extraction design for later refinement, not delivered behavior.

**Scope:** design only, initially grounded in Foundation 8805e95be, CShells 352a25e and Nuplane source snapshot 21e2c24. The deferred freshness review below uses locally qualified CShells #147 at 8b22edc and the exact Nuplane .94 package/source bf27be6. These CShells changes remain unpublished. This research changes no production files and runs no builds; prerequisite implementation evidence is recorded separately in the [register](../plans/modular-hosting-upstream/evidence.md).

## Recommendation

Add one optional CShells.Nuplane package, downstream of CShells and Nuplane loading/observer abstractions. Runtime seams use CShells.Abstractions; the explicit composition helper may reference CShells for its builder and host-sharing API, as the existing ASP.NET Core integration does. Keep CShells core free of Nuplane references and leave existing provider composition unchanged when the package is absent.

Minimal public types:

1. NuplaneFeatureAssemblyProvider : IFeatureAssemblyProvider resolves the root IPackageAssemblyCatalog, awaits GetPackagedAssembliesAsync(ct), and flattens each result's Assemblies. Never load from AssemblyReferences: Nuplane documents them as durable file references, which include shared copies excluded from the loaded assembly list (Nuplane src/Nuplane.Loading.Abstractions/IPackageAssemblyCatalog.cs:5-22, 48-72, snapshot 21e2c24).
2. NuplaneFeatureCatalogObserver : INuplaneObserver is registered after AutoloadPackages; it observes OnPackagesReconciledAsync, refreshes the CShells runtime feature catalog, and applies separately configured refresh-trigger and auto-reload behavior. Existing WithAssemblyProvider<T>(), WithAssemblies(...), and custom providers remain valid (CShells src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs:5-25, src/CShells/DependencyInjection/CShellsBuilderExtensions.cs:122-149, snapshot 352a25e).

An opt-in composition helper can register these types and select the provider, but should not hide provider precedence or create a second reconcile coordinator. Keep Elsa configuration keys, manifest policy, EF refusal interpretation, and command formatting in Elsa.

## Reconciliation behavior and compatibility

The observer must use OnPackagesReconciledAsync, after Nuplane autoloading. Both Foundation Host and Workbench register after AutoloadPackages (Foundation src/apps/Elsa.Foundation.Host/Program.cs:38-57, Workbench src/apps/Elsa.Workbench/Program.cs:221-225, Foundation snapshot 8805e95be). Nuplane's catalog returns only active, loaded, discoverable packages, and returns empty when loading is unavailable (Nuplane src/Nuplane.Loading/PackageAssemblyCatalog.cs:14-55, snapshot 21e2c24). The .94 completion-dispatch gap below must be corrected upstream before this bridge can satisfy package-removal delivery.

### Completion prerequisite: removal to zero

The exact .94 [HealthAndMetricsMiddleware](https://github.com/valence-works/nuplane/blob/bf27be646d4c124b6b2ba2c632f9a49b1a5252c6/src/Nuplane/Reconciliation/Middleware/HealthAndMetricsMiddleware.cs#L48) publishes Reconciled only when `AppliedPackages.Count > 0`; current source `21e2c24` has the same condition. An empty desired set can commit removal of the last active package while returning no applied packages. Changed is emitted, but the loading observer and both Elsa bridges do their work only at Reconciled, so the CShells snapshot can remain stale. Nuplane's load-state catalog reads authoritative active state and can report empty even while the skipped loader retirement pass leaves old contexts rooted; an empty loading catalog is not proof of collection.

[Nuplane #109](https://github.com/valence-works/nuplane/issues/109), a native child/dependency of this integration feature, owns the minimal existing-event correction: publish completion when the applied list is nonempty **or committed removals exist**, retaining an empty applied list for pure removal. Empty idle and failed-only cycles retain their existing no-completion behavior. Removal plus failed remaining acquisitions must still notify without changing failure/degraded accounting. Focused middleware/loader tests and a real reconciliation-to-empty-state observer regression must prove ordering and post-commit state; reverting only the condition must fail the causal test.

This task changes notification eligibility, not collection policy. Foundation #2362 retains actual unload/retire ownership, with no duplicate unloading in this adapter. The final adapter/consumer proof must use a released Nuplane preview containing #109; .94 remains the inspected baseline, not a sufficient final dependency. Foundation-style "every reconcile" means every received eligible completion callback, not every idle pipeline invocation.

Do not collapse enablement, catalog refresh trigger, and automatic reload into one ReloadOnPackageChange boolean. The current host behaviors differ:

- Foundation Host defaults enabled. When enabled, it refreshes and attempts active-shell reload after every received completion callback, including unchanged cycles with applied packages. Its setting disables both refresh and reload; there is no retained dirty/retry state (Foundation src/apps/Elsa.Foundation.Host/Shells/ShellReloadOnPackagesChanged.cs:22-53).
- Workbench refreshes only added/updated/removed changes or previously pending work. Its auto-reload setting defaults false; false still refreshes and clears pending work after a successful refresh, so a later explicit reload sees the new catalog (Foundation src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs:39-78).

Represent those choices independently, using narrow configuration/options or policies for (a) observer enablement, (b) refresh trigger, and (c) whether refresh implies automatic shell reload. Configure Foundation Host to preserve its every-reconcile enabled default and Workbench to preserve changed-or-pending refresh with automatic reload disabled by default. Avoid adding a broad framework if host configuration over existing APIs suffices.

For a changed-or-pending trigger, clear dirty state only after catalog refresh succeeds and, when reload was requested, every active shell reload succeeds. Inspect all ReloadResult.Error values because batch reload failures can be returned rather than thrown. Keep dirty work on refresh/reload failure so a later eligible reconcile retries. Propagate caller cancellation. Nuplane dispatches observers sequentially in registration order and catches/logs observer exceptions independently (Nuplane src/Nuplane/Events/ObserverEventDispatcher.cs:10-28, 68-84, snapshot 21e2c24). The current Workbench bool relies on serialized reconciliation; verify the actual targeted package contract before relying on that assumption. If callbacks can overlap, use a single drainer with versioned dirty state so work arriving during a refresh/reload is not cleared by the older attempt.

Both existing observers check for an active shell before doing anything. Workbench checks before it marks _changePending (Foundation src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs:50-58). A reconcile during a no-active interval is therefore forgotten. A failed prior activation may already have initialized a stale catalog; the next builder's EnsureInitializedAsync does not refresh that snapshot. The adapter will intentionally retain this freshness work while preserving lazy activation, as specified below.

## Deferred catalog freshness

Use one private root coordinator shared by the observer and the #147 build participant. Track a source-change epoch and a successfully refreshed catalog epoch. Keep active-shell reload retry work separate. Register the same instance through both contracts; this case needs no lifecycle subscriber or attempted-generation map.

1. Record eligible reconcile work before checking for an active shell. With no active shell, retain the source epoch and return without scanning or activating anything. Preserve observer enablement and each host's refresh trigger.
2. BeginAsync runs before the builder's first catalog read. Under a shared refresh semaphore, compare the epochs. If source work is outstanding, capture epoch E, call RefreshAsync, and advance the catalog watermark only to E after success. A failure/cancellation leaves work outstanding. Refresh at this explicit build request also covers an uninitialized catalog with one scan; the builder's subsequent EnsureInitializedAsync is a no-op.
3. Active observer passes use the same refresh gate and epoch capture. Release that gate before ReloadActiveAsync: reload invokes BeginAsync, and holding the gate would deadlock. Keep observer passes serialized or otherwise version reload work so an older success cannot clear a newer request/failure.
4. Create automatic reload retry work only when an eligible observer pass actually targets active shells. Clear it only after the applicable full-success condition; inspect all returned reload errors. A deferred catalog refresh does not itself request a reload of the first shell later activated from that fresh catalog. Workbench reload-off still refreshes.

Catalog freshness is independent of promotion success. If refresh succeeds and candidate activation fails, the next Begin can reuse the fresh catalog without rescanning. A later reconcile advances the source epoch and requires another refresh. Source changes arriving during a refresh remain outstanding because only its captured epoch is acknowledged.

An already-overlapping build can still select an older snapshot when a reconcile arrives after Begin's refresh. Retain the newer epoch for the next build or eligible observer pass. This design does not invalidate in-flight builds or promise that every overlapping candidate serves the latest package set; adding an Active acknowledgment would not establish that stronger guarantee either. Generation protection remains the separate #147/#2164 boundary.

This deliberately fixes forgotten no-active work while preserving deferred scanning. Refreshing directly during every inactive reconcile would also fix staleness, but would eagerly scan and retain assembly snapshots for hosts that may never request a shell. The Begin seam avoids that timing change.

## Root registration and aliases

The exact Nuplane .94 source's [OnPackagesChanged implementation](https://github.com/valence-works/nuplane/blob/bf27be646d4c124b6b2ba2c632f9a49b1a5252c6/src/Nuplane/Builder/NuplaneBuilder.cs#L138) registers INuplaneObserver by implementation type. Despite its XML wording, it does not alias a separately registered concrete singleton. Registering the coordinator concretely and also calling OnPackagesChanged for it would create two independent state owners.

Use one concrete coordinator singleton and explicit singleton observer/build-participant factory aliases resolving that concrete service. Add the observer alias after AutoloadPackages to preserve ordering. The build-participant interface is already excluded from shell copies. Select the concrete coordinator with ShareSingletonWithShells so a copied observer factory also resolves the root instance; do not implicitly share every other INuplaneObserver registration. Its injected package assembly catalog is then root-resolved, without HostContainer capture.

Keep this coordinator non-disposable while it owns only managed synchronization state. A copied interface factory returning a borrowed disposable can acquire child disposal ownership even when its concrete service registration is shared. If the adapter later owns teardown resources, review that alias/disposal boundary explicitly. Tests must prove root observer/participant identity, shell-side alias identity, unchanged unrelated observers, and safe drain of overlapping generations.

## Elsa reload policy and refusal boundaries

The generic observer may accept a package-change policy to decide whether an already-refreshed catalog should trigger automatic reload. The seam must not decide whether catalog refresh happens. Foundation #2258 / ADR 0079 keeps per-package hot-reload, restart, and locked policy in Foundation. A generic reload policy alone cannot enforce restart: if the new package is visible in Nuplane's active set and the catalog is refreshed, a manual shell reload may still compose it. Foundation must stage/exclude restart-policy packages until restart; locked enforcement also stays in Foundation. Do not describe auto-reload-off as restart-only behavior.

Keep IEfModuleRefusal handling in Elsa. Workbench unwraps nested refusals, replaces the host placeholder, logs a warning, and retains work for retry (Foundation src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs:87-117). The generic integration should surface shell reload errors without interpreting Elsa types. Preserve this Workbench adapter during consolidation.

## Snapshot ownership limit

The local #146 catalog-commit notification is an observation API, not an activation-lifetime guarantee. In CShells 352a25e, RuntimeFeatureCatalog.RefreshAsync commits a snapshot and notifies outside the refresh lock (src/CShells/Features/RuntimeFeatureCatalog.cs:40-96; capability contract src/CShells.Abstractions/Features/IRuntimeFeatureCatalogCommitSource.cs:3-26). ShellProviderBuilder.BuildAsync captures CurrentSnapshot once at build start (src/CShells/Lifecycle/ShellProviderBuilder.cs:48-61). A post-commit observer/provider does not protect assembly readability if Nuplane unloads a context while candidate build/initializers still use its assemblies. Foundation #2164 remains the generation-ownership work; do not claim this package solves it or duplicate Foundation #2362 unload behavior.

## Package/source gate

Foundation pins Nuplane packages to 0.0.11-preview.94 in Directory.Packages.props:118-122; Foundation Host and Workbench lock files resolve that version. The cached nuspec for both Nuplane.Abstractions and Nuplane.Loading.Abstractions records source commit bf27be646d4c124b6b2ba2c632f9a49b1a5252c6. Root's GitHub comparison of base bf27be6 to head 21e2c24 found the supplied source snapshot 21e2c24 is four commits ahead of the published source commit bf27be6, with no commits behind; among the relevant abstractions, package auto-loader, and dispatcher files, the only changed file found was ResolvedPackage.cs, with an additive PackageContentHash init property. The assembly-catalog and observer contracts are unchanged in that comparison. Target the exact published .94 package for the adapter and prove compatibility through restore/build; do not claim the supplied snapshot is source-identical.

CShells snapshot 352a25e has no Nuplane reference/lock entry and its current CI/publish flows do not build a Nuplane adapter. Add the optional package to its intended pack/publish gate and add a consumer/integration proof. Preserve dependency direction: no Nuplane-to-CShells reference.

## Deterministic acceptance proof

1. Provider uses active loaded Assemblies, never assembly-reference paths; test empty/unavailable catalog, cancellation, and deterministic order where guaranteed.
2. Real composition registers observer after autoloading; a newly loaded package appears in the catalog refresh from that reconcile.
3. Test separate host modes: Foundation-style every-reconcile refresh/reload default; Workbench-style changed-or-pending refresh and reload-off default. Changed/unchanged cycles must match each mode.
4. For changed-or-pending mode, test refresh failure, partial batch reload failure, refusal as generic error, and successful retry on a later unchanged reconcile. Verify reload-off still refreshes. Verify cancellation.
5. Fail an initial activation after catalog initialization, then reconcile with no active shell. Assert no immediate refresh/reload; the next real build refreshes once before snapshot selection. Promotion failure after that refresh must not force another scan; a newer reconcile must. Cover the cold catalog path with exactly one scan and no gratuitous reload of the first successful shell.
6. Confirm one observer failure does not suppress later observers. Add concurrency test only if the targeted Nuplane callback contract allows overlap; ensure an intervening change remains pending.
7. Omitting CShells.Nuplane leaves existing configuration alone; host/explicit/custom providers still compose; CShells core has no Nuplane dependency and integration has no Elsa dependency.
8. Document the #2164 generation-readability limit and Foundation-only restart/locked policy boundary.
9. Gate a source change during refresh and after Begin but before snapshot selection. Only the captured epoch is acknowledged; the next Begin refreshes outstanding work. Test refresh failure/cancellation retention and reentrant reload without holding the refresh gate. Do not assert in-flight build invalidation.
10. Prove one coordinator across root observer/build-participant aliases and shell-resolved observer aliases, without modifying unrelated observers or transferring disposal ownership. Registration must preserve ordering after Nuplane autoload; no HostContainer capture is needed.

Existing Foundation proof is in tests/essentials/Modularity/Tests/WorkbenchShellCatalogRefreshTests.cs:40-183 (no active shell, changed/unchanged, opt-in reload, refresh/reload failure, refusal details/retry, cancellation). Ordering against real Workbench composition is pinned in tests/essentials/Modularity/Tests/HostOwnedServicesAreSharedWithShellsTests.cs:264-283. Extend/generalize this proof without moving Elsa refusal assertions into the generic package.

## Decisions and boundaries

- Keep the optional integration as one CShells.Nuplane package; existing APIs support the minimal adapter without coupling CShells core to Nuplane.
- Preserve current per-host observer enablement, refresh trigger, and reload defaults via explicit settings. Intentionally retain no-active freshness work until the next requested build; keep its catalog acknowledgment separate from active-shell reload retries.
- Restart-policy staging remains Foundation-owned; it cannot be met by disabling automatic reload.
- Use the exact published Nuplane .94 package/source. Current evidence distinguishes its source commit bf27be646d4c124b6b2ba2c632f9a49b1a5252c6 from the supplied 21e2c24 snapshot; compatibility should be proven by restore/build.
