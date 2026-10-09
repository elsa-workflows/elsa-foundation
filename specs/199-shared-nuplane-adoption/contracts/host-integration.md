# Host Integration Contract

## Profiles

| Host | Enabled | Refresh trigger | AutoReload |
|---|---|---|---|
| Foundation.Host | Existing key parsed, absent/invalid true | EveryEligibleCompletion | true |
| Workbench | true | ChangedOrPending | Existing key parsed, absent/invalid false |

Existing key: `Elsa:Shells:ReloadOnPackageChange`. Map it through standard `IConfigureOptions<NuplaneIntegrationOptions>` and `ConfigurationChangeTokenSource<NuplaneIntegrationOptions>`; the change source watches `Elsa:Shells`. A configuration root Reload must affect later observer deliveries without rebuilding the service provider. In-flight delivery retains its upstream copy. Changing a setting alone schedules no callback or reload; the next eligible package completion consumes the updated policy.

## Composition

Nuplane autoload and other preexisting observers register first. The CShells builder then calls `WithNuplaneFeatureDiscovery`; retain other host assembly providers. Remove both app-local Nuplane providers and old observer registrations after verification. The public observer/participant aliases identify the same root coordinator; shell observer resolution borrows it. Do not register the internal coordinator directly or capture an already-built host provider.

## Diagnostics

The app-owned result callback reports every shell failure and excludes failures from successful counts. Foundation keeps `ShellReloadFailure` including private shared-interface reading, sanitized public details and quoted host-directory substitution. Workbench retains nested/aggregate refusal detection and its existing warning/error log behavior. Callbacks do not create scheduling state or alter raw results.

Ordinary thrown refresh/reload failure remains observable at Error and best-effort at Nuplane delivery. The correction is delivered and public-package-qualified at CShells `.171` with Nuplane `.99`: actual registered composition logs Error with the original exception/correlation, rethrows, and Nuplane emits its existing Warning while continuing to later observers. Exact old ordinary-failure log-count parity is not claimed. Foundation adoption must validate its own registered composition and Elsa diagnostics. Caller cancellation remains a direct-observer test with no failure logging; it is distinct from actual dispatcher isolation of ordinary callback failures. The public preview consumer is upstream evidence only, not Foundation host proof or stable acceptance.

## Acceptance boundary

Keep the canonical preparation branch's `.159`/`.94` pins and locks unchanged. In the owned qualification copy, use the audited `.171`/`.99` family with an isolated central version overlay and lockfiles; do not use conditional or project-local version override paths. Stable Nuplane then complete CShells family, normal central pins/reached locks/maps, actual Host/Workbench regression and backend E2E plus frozen demo Acts1/2 rehearsal are required for final acceptance. Preview preparation is explicitly insufficient. Separate startup/readiness/custom-registry policy, generation readability, unloading and pruning contracts remain unchanged.
