# Optional CShells-Nuplane Integration Candidate

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md); [CShells #142](https://github.com/valence-works/cshells/issues/142). Status: draft extraction design for later refinement, not delivered behavior.

**Scope:** design only, grounded in Foundation 8805e95be, CShells 352a25e (local #147 working snapshot), and Nuplane source snapshot 21e2c24. The #146 catalog-commit notification exists only in the local CShells working tree; it is unpublished. No production files changed and no builds were run.

## Recommendation

Add one optional CShells.Nuplane package, downstream of CShells.Abstractions and Nuplane loading/observer abstractions. Keep CShells core free of Nuplane references and leave existing provider composition unchanged when the package is absent.

Minimal public types:

1. NuplaneFeatureAssemblyProvider : IFeatureAssemblyProvider resolves the root IPackageAssemblyCatalog, awaits GetPackagedAssembliesAsync(ct), and flattens each result's Assemblies. Never load from AssemblyReferences: Nuplane documents them as durable file references, which include shared copies excluded from the loaded assembly list (Nuplane src/Nuplane.Loading.Abstractions/IPackageAssemblyCatalog.cs:5-22, 48-72, snapshot 21e2c24).
2. NuplaneFeatureCatalogObserver : INuplaneObserver is registered after AutoloadPackages; it observes OnPackagesReconciledAsync, refreshes the CShells runtime feature catalog, and applies separately configured refresh-trigger and auto-reload behavior. Existing WithAssemblyProvider<T>(), WithAssemblies(...), and custom providers remain valid (CShells src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs:5-25, src/CShells/DependencyInjection/CShellsBuilderExtensions.cs:122-149, snapshot 352a25e).

An opt-in composition helper can register these types and select the provider, but should not hide provider precedence or create a second reconcile coordinator. Keep Elsa configuration keys, manifest policy, EF refusal interpretation, and command formatting in Elsa.

## Reconciliation behavior and compatibility

The observer must use OnPackagesReconciledAsync, after Nuplane autoloading. Both Foundation Host and Workbench register after AutoloadPackages (Foundation src/apps/Elsa.Foundation.Host/Program.cs:38-57, Workbench src/apps/Elsa.Workbench/Program.cs:221-225, Foundation snapshot 8805e95be). Nuplane's catalog returns only active, loaded, discoverable packages, and returns empty when loading is unavailable (Nuplane src/Nuplane.Loading/PackageAssemblyCatalog.cs:14-55, snapshot 21e2c24).

Do not collapse enablement, catalog refresh trigger, and automatic reload into one ReloadOnPackageChange boolean. The current host behaviors differ:

- Foundation Host defaults enabled. When enabled, it refreshes and attempts active-shell reload after every reconcile, including unchanged cycles. Its setting disables both refresh and reload; there is no retained dirty/retry state (Foundation src/apps/Elsa.Foundation.Host/Shells/ShellReloadOnPackagesChanged.cs:22-53).
- Workbench refreshes only added/updated/removed changes or previously pending work. Its auto-reload setting defaults false; false still refreshes and clears pending work after a successful refresh, so a later explicit reload sees the new catalog (Foundation src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs:39-78).

Represent those choices independently, using narrow configuration/options or policies for (a) observer enablement, (b) refresh trigger, and (c) whether refresh implies automatic shell reload. Configure Foundation Host to preserve its every-reconcile enabled default and Workbench to preserve changed-or-pending refresh with automatic reload disabled by default. Avoid adding a broad framework if host configuration over existing APIs suffices.

For a changed-or-pending trigger, clear dirty state only after catalog refresh succeeds and, when reload was requested, every active shell reload succeeds. Inspect all ReloadResult.Error values because batch reload failures can be returned rather than thrown. Keep dirty work on refresh/reload failure so a later eligible reconcile retries. Propagate caller cancellation. Nuplane dispatches observers sequentially in registration order and catches/logs observer exceptions independently (Nuplane src/Nuplane/Events/ObserverEventDispatcher.cs:10-28, 68-84, snapshot 21e2c24). The current Workbench bool relies on serialized reconciliation; verify the actual targeted package contract before relying on that assumption. If callbacks can overlap, use a single drainer with versioned dirty state so work arriving during a refresh/reload is not cleared by the older attempt.

Both existing observers check for an active shell before doing anything. Workbench checks before it marks _changePending (Foundation src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs:50-58). A reconcile during a no-active interval is therefore forgotten. Do not assume the next activation reads the latest assemblies: a failed prior activation may already have initialized a stale catalog. Record this as a compatibility/design case and explicitly test/decide it; do not silently change it during consolidation.

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
5. Test no-active reconcile followed by activation after a failed activation initialized the catalog: make the current lost-change behavior explicit, then test the selected behavior if consolidation intentionally changes it.
6. Confirm one observer failure does not suppress later observers. Add concurrency test only if the targeted Nuplane callback contract allows overlap; ensure an intervening change remains pending.
7. Omitting CShells.Nuplane leaves existing configuration alone; host/explicit/custom providers still compose; CShells core has no Nuplane dependency and integration has no Elsa dependency.
8. Document the #2164 generation-readability limit and Foundation-only restart/locked policy boundary.

Existing Foundation proof is in tests/essentials/Modularity/Tests/WorkbenchShellCatalogRefreshTests.cs:40-183 (no active shell, changed/unchanged, opt-in reload, refresh/reload failure, refusal details/retry, cancellation). Ordering against real Workbench composition is pinned in tests/essentials/Modularity/Tests/HostOwnedServicesAreSharedWithShellsTests.cs:264-283. Extend/generalize this proof without moving Elsa refusal assertions into the generic package.

## Decisions and boundaries

- Keep the optional integration as one CShells.Nuplane package; existing APIs support the minimal adapter without coupling CShells core to Nuplane.
- Preserve current per-host observer enablement, refresh trigger, and reload defaults via explicit settings. Dirty retries and retaining changes across no-active intervals are behavior decisions, not incidental consequences of a shared implementation.
- Restart-policy staging remains Foundation-owned; it cannot be met by disabling automatic reload.
- Use the exact published Nuplane .94 package/source. Current evidence distinguishes its source commit bf27be646d4c124b6b2ba2c632f9a49b1a5252c6 from the supplied 21e2c24 snapshot; compatibility should be proven by restore/build.
