# Research: Shared Nuplane Adapter Adoption

Source baselines: Foundation main `12d24a6bb41c20a9b20bfa44642c0146e8d43f2c`; upstream adapter product/public `.169` commit `c29b3eb86e3e68fb18003d0604bddb1f7314d8e3`, unchanged in CShells main `c39f191bed88ff7c6153125e83bf83d9d9b393d3`; Nuplane `.99`/main `eb2cf6c2ee1f79dc2c45fb83cc415bbe4856d0d4`.

## Host-safe composition placement

**Decision**: Keep the two small host-specific option mappings and diagnostic callbacks app-local, and call the same public upstream `WithNuplaneFeatureDiscovery` in both host compositions. Add only the optional upstream package to each host's direct dependency graph. Keep existing `Elsa.Modularity.Nuplane` feature projection services unchanged.

**Rationale**: Foundation.Host currently has no reference to `Elsa.Modularity.Nuplane`. Adding its feature-management and package-manifest dependencies solely to share a few configuration lines expands the host closure. Framework §2.17 prefers a few duplicated mechanical lines when sharing would add a transitive dependency. The shared provider/observer/state machine moves upstream; remaining profile differences are intentional. No new Elsa package, source linking or generic host abstraction is needed, and #2354 retains host-library extraction.

**Alternatives considered**: Existing `Elsa.Modularity.Nuplane` helper (earlier proposal), a new hosting package, and shared compiled source. Each introduces a wider dependency or ownership surface than the host-specific mechanical mapping needs. This refines helper placement; it does not alter program D5/D9 or the sole upstream coordinator requirement.

## Live legacy configuration

**Decision**: Use each application's standard `IConfigureOptions<NuplaneIntegrationOptions>` implementation and a default-name `ConfigurationChangeTokenSource<NuplaneIntegrationOptions>` bound to the existing `Elsa:Shells` configuration section. Register through normal options DI. The configure implementation reads the existing raw key with `bool.TryParse` so malformed values preserve their host-specific fallback. Set the fixed refresh profile and attach the Elsa result callback in the same options instance.

**Rationale**: A dependency-aware configure delegate alone does not invalidate cached monitored options. Binding arbitrary configuration directly into upstream options would also expose unrelated option names and reject malformed booleans differently. The stock change-token source supplies invalidation without a custom subscription or another observer. The upstream coordinator copies the options once per eligible completion.

**Alternatives considered**: Capturing startup options, rebuilding the provider, custom monitors, or a second Elsa observer. These violate live behavior or duplicate upstream coordination.

## Existing regression continuity and delivery boundary

**Finding**: `WorkbenchShellCatalogRefreshTests` directly constructs the old observer and expects ordinary refresh/thrown-reload failures to be caught and logged once at Error, with retry on a later eligible completion. Its cancellation case directly invokes the observer and expects caller cancellation to propagate with no warning/error. Other assertions cover per-shell refusal warnings, non-refusal Error, unchanged-cycle behavior and reload counts. Preserve all existing test methods and assertions; repair setup/wiring only.

The upstream coordinator propagates ordinary callback failures. Nuplane's real `ObserverEventDispatcher.PublishReconciledAsync` isolates them and continues later observers, but its default `ReconciliationLogger.LogObserverError` is Warning and carries only `Exception.Message`. Therefore changing the fixture to the real delivery boundary preserves no-throw/retry but does not preserve the existing Error assertion by itself. Do not weaken it or insert a test-only catch/logger shim. The completed bounded public-package diagnostic confirmed this gap for catalog-refresh failure; it did not test reload failure or the proposed correction.

**Root-selected narrow correction, confirmed by the bounded diagnostic and independent design review but not yet implemented or qualified**: generic internal upstream error logging at the coordinator's observer boundary, including the original exception and correlation, followed by rethrow to Nuplane. Use ordinary logging abstractions and a no-logger fallback; no new public options, state or Elsa dependency. The upstream holder factory manually constructs the coordinator, so it must explicitly pass the optional DI logger; adding a constructor parameter alone is insufficient. Declare the logging abstraction dependency directly if needed, and prove actual registered composition captures the Error/original exception. Exclude caller cancellation and the existing fatal-runtime filter. Keep Begin/build exception behavior and Nuplane dispatcher isolation unchanged. Existing upstream tests explicitly require the original thrown refresh, registry and result-callback exception (`NuplaneRefreshCoordinatorTests` and `NuplaneReloadResultsTests`); swallowing these errors would regress qualified upstream behavior. Log-and-rethrow therefore preserves both upstream throw assertions and Foundation Error assertions. Nuplane additionally emits its existing generic Warning; that additional warning is recorded, rather than represented as exact diagnostic parity. Per-shell returned refusals still have only the Elsa-owned warning/reporting callback. This is a reusable observability prerequisite, not a second host observer.

## Package qualification and final adoption

**Decision**: Preserve the committed `.159`/`.94` central versions until final released-family adoption. Prepare source with the future package dependency declared, and use a separate owned qualification copy for interim `.169`/`.99` pins and changed locks. Record those exact preview identities and never claim unchanged-pin compilation or stable acceptance.

**Rationale**: The current pins cannot supply the adopted APIs. Prior #2164 preparation follows the same distinction. Final Nuplane stable publication precedes the adapter's stable dependency update and full CShells stable family; then Foundation pins, reached locks, maps, rebuilt hosts, backend E2E and demo rehearsal are qualified together.

## Deferred separate work

Startup/readiness policy (#143), generation readability (#2164), host-library extraction (#2354), actual unload (#2362) and destructive pruning (#108) are unchanged. The shared-root admission choice and custom-registry readiness choice do not invalidate this observer preparation. No new owner decision is requested here.
