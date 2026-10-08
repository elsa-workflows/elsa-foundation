# Host Integration Contract

## Profiles

| Host | Enabled | Refresh trigger | AutoReload |
|---|---|---|---|
| Foundation.Host | Existing key parsed, absent/invalid true | EveryEligibleCompletion | true |
| Workbench | true | ChangedOrPending | Existing key parsed, absent/invalid false |

Existing key: `Elsa:Shells:ReloadOnPackageChange`. Map it through standard `IConfigureOptions<NuplaneIntegrationOptions>` and `ConfigurationChangeTokenSource<NuplaneIntegrationOptions>`; the change source watches `Elsa:Shells`. A configuration root Reload must affect later observer deliveries without rebuilding the service provider. In-flight delivery retains its upstream copy.

## Composition

Nuplane autoload and other preexisting observers register first. The CShells builder then calls `WithNuplaneFeatureDiscovery`; retain other host assembly providers. Remove both app-local Nuplane providers and old observer registrations after verification. The public observer/participant aliases identify the same root coordinator; shell observer resolution borrows it. Do not register the internal coordinator directly or capture an already-built host provider.

## Diagnostics

The app-owned result callback reports every shell failure and excludes failures from successful counts. Foundation keeps `ShellReloadFailure` including private shared-interface reading, sanitized public details and quoted host-directory substitution. Workbench retains nested/aggregate refusal detection and its existing warning/error log behavior. Callbacks do not create scheduling state or alter raw results.

Ordinary thrown refresh/reload failure must remain observable at Error and best-effort at Nuplane delivery; the completed public-package diagnostic confirmed that catalog-refresh failure currently reaches the dispatcher only as its generic Warning. The root-selected correction adds generic upstream Error logging before rethrow, preserving the existing upstream exception contract. The upstream holder factory must explicitly pass its optional DI logger into the manually constructed coordinator, with no-logger fallback; validate the registered observer rather than only direct construction. The actual Nuplane dispatcher also emits its generic Warning; exact old ordinary-failure log-count parity is not claimed. Implementation readiness remains pending the upstream correction, actual-DI regression tests and package qualification; the diagnostic probe and independent design review are complete. Caller cancellation remains direct-observer cancellation with no failure logging; actual dispatcher isolation is an existing independent boundary.

## Acceptance boundary

Stable Nuplane then complete CShells family, exact pins/reached locks/maps, actual Host/Workbench regression and backend E2E plus frozen demo Acts1/2 rehearsal are required. Interim preview preparation is explicitly insufficient. Separate startup/readiness/custom-registry policy, generation readability, unloading and pruning contracts remain unchanged.
