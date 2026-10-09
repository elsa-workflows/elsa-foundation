# Research: Shared Nuplane Adapter Adoption

Source baselines: Foundation main at preparation start `7357b91e012c56cb34f1259ef7a60ef56e144afe`; CShells observer correction source `99b41baf08cd40fee0323478e02a543549a57621`, runtime-qualified as `.171`; Nuplane `.99`/main `eb2cf6c2ee1f79dc2c45fb83cc415bbe4856d0d4`. The canonical Foundation pins remain CShells `.159` and Nuplane `.94`. Public `.171` archive and adapter-consumer evidence is recorded at `cshells-published-preview-171/feedz-audit/package-audit.json` and `cshells-observer-diagnostics/public171-consumer-attempt2/root-qualification.json`; `.172` has an archive audit only at `cshells-published-preview-172/feedz-audit/package-audit.json`. All are under the modular-hosting artifact root. `.171` is preview-only upstream consumer evidence, not Foundation host adoption; `.172` archive identity does not substitute for a runtime consumer proof.

## Host-safe composition placement

**Decision**: Keep the two small host-specific option mappings and diagnostic callbacks app-local, and call the same public upstream `WithNuplaneFeatureDiscovery` in both host compositions. Add only the optional upstream package to each host's direct dependency graph. Keep existing `Elsa.Modularity.Nuplane` feature projection services unchanged.

**Rationale**: Foundation.Host currently has no reference to `Elsa.Modularity.Nuplane`. Adding its feature-management and package-manifest dependencies solely to share a few configuration lines expands the host closure. Framework §2.17 prefers a few duplicated mechanical lines when sharing would add a transitive dependency. The shared provider/observer/state machine moves upstream; remaining profile differences are intentional. No new Elsa package, source linking or generic host abstraction is needed, and #2354 retains host-library extraction.

**Alternatives considered**: Existing `Elsa.Modularity.Nuplane` helper (earlier proposal), a new hosting package, and shared compiled source. Each introduces a wider dependency or ownership surface than the host-specific mechanical mapping needs. This refines helper placement; it does not alter program D5/D9 or the sole upstream coordinator requirement.

## Live legacy configuration

**Decision**: Use each application's standard `IConfigureOptions<NuplaneIntegrationOptions>` implementation and a default-name `ConfigurationChangeTokenSource<NuplaneIntegrationOptions>` bound to the existing `Elsa:Shells` configuration section. Register through normal options DI. The configure implementation reads the existing raw key with `bool.TryParse` so malformed values preserve their host-specific fallback. Set the fixed refresh profile and attach the Elsa result callback in the same options instance.

**Rationale**: A dependency-aware configure delegate alone does not invalidate cached monitored options. Binding arbitrary configuration directly into upstream options would also expose unrelated option names and reject malformed booleans differently. The stock change-token source supplies invalidation without a custom subscription or another observer. The upstream coordinator copies the options once per eligible completion.

**Alternatives considered**: Capturing startup options, rebuilding the provider, custom monitors, or a second Elsa observer. These violate live behavior or duplicate upstream coordination.

Configuration invalidation is not itself a work trigger. Changing Workbench auto-reload from false to true (or the reverse) affects the policy captured by the next eligible package completion; it schedules no immediate refresh/reload and creates no pending package work. A setting-toggle test must deliver the next eligible completion after changing configuration rather than expect work from the change token alone.

## Existing regression continuity and delivery boundary

**Finding**: `WorkbenchShellCatalogRefreshTests` directly constructs the old observer and expects ordinary refresh/thrown-reload failures to be caught and logged once at Error, with retry on a later eligible completion. Its cancellation case directly invokes the observer and expects caller cancellation to propagate with no warning/error. Other assertions cover per-shell refusal warnings, non-refusal Error, unchanged-cycle behavior and reload counts. Preserve all existing test methods and assertions; repair setup/wiring only.

The upstream coordinator propagates ordinary callback failures. Nuplane's real `ObserverEventDispatcher.PublishReconciledAsync` isolates them and continues later observers while its default `ReconciliationLogger.LogObserverError` emits Warning. Tests moved to the actual delivery boundary must preserve the no-throw/retry objective without a test-only catch/logger shim. The existing direct-observer cancellation case remains direct: caller cancellation propagates without a failure log, distinct from ordinary exception isolation at dispatch.

**Delivered correction and qualified boundary**: the coordinator logs an ordinary observer-callback failure with original exception and correlation before rethrowing to Nuplane; Nuplane's actual dispatcher emits its existing Warning, isolates the observer failure, and continues to later observers. The manually constructed holder passes the optional DI logger. The public `.171` package consumer with Nuplane `.99` exercises registered composition, Error/original-exception/correlation, Error-before-Warning order, following-observer delivery and retained retry on .NET 8/9/10. This preserves the upstream thrown-exception contract and the host's best-effort delivery objective, while documenting the additional Warning rather than claiming exact old log-count parity. Archive audit and six consumer scenarios per runtime prove the upstream boundary only; Foundation composition, Elsa callbacks and actual host behavior remain to be verified. Per-shell returned refusals still use the Elsa-owned callback.

## Package qualification and final adoption

**Decision**: Keep the canonical preparation branch documentation-only; leave its `.159`/`.94` central versions, project references and lockfiles unchanged. Prepare candidate source and direct `CShells.Nuplane` references in a separately owned qualification worktree/copy. In that copy only, add a coherent central preview overlay for the complete CShells `.171` family (including the adapter) and Nuplane `.99`, then regenerate reached locks and qualify. Preserve exact source, package, cache and lock provenance. Do not use `VersionOverride`, conditional versions or another parallel pin path.

**Rationale**: Current pins cannot supply the adopted APIs, while changing canonical defaults to a preview would make routine builds depend on an interim package family. A disposable full-repository qualification copy gives a concrete, locked restore/build target without changing those defaults. When stable versions are available, replay the reviewed application-source diff into the final adoption branch, add normal centrally managed stable versions, update reached locks and maps, and qualify rebuilt hosts, backend E2E and demo rehearsal together. Preview qualification is not merge or release acceptance.

## Deferred separate work

Startup/readiness policy (#143), generation readability (#2164), host-library extraction (#2354), actual unload (#2362) and destructive pruning (#108) are unchanged. The shared-root admission choice and custom-registry readiness choice do not invalidate this observer preparation. No new owner decision is requested here.
