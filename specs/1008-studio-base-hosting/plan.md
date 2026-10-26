# Implementation Plan: Reusable Studio base hosting

**Branch**: `agent/codex/3357-studio-hosting` | **Date**: 2026-10-26

**Spec**: [spec.md](spec.md) · **Issue**: [#3357](https://github.com/elsa-workflows/elsa-foundation/issues/3357)

## Summary and technical context

Create the Razor library `Studio/src/essentials/Elsa.Studio.Hosting`, targeting net10.0 and the existing
Studio version/metadata line. Reference Api/Core and CShells.AspNetCore 0.0.31 only. Use public CShells
blueprint, resolver and tracked-scope APIs. No frontend build is introduced here; #3361 owns moving
Client and bundled static assets. Tests use real files and a separately named fallback document.

Storage: application-owned optional shells.json, resolved from ContentRoot. No database or new file
provider. Testing: the existing xUnit Studio project and TestServer, whole-suite qualification,
causal controls for cold authorization/selected routing/scope retention, architecture/maps/locked restore.
No new test project or child-host tier classification is needed. No throughput/performance target is added.

## Constitution check

Pre-design and post-design checks pass against both current constitutions and the repository review checklist:

- Framework §§2.1/2.6/2.8: compose existing public framework contracts; no backend or optional implementation
  closure, duplicate discovery registry or production friend grant.
- §§2.2/2.23.3: only consumer composition/options/coordinator seams are public. Keep endpoint, runtime,
  resolver, startup validator and middleware implementations internal. Track the new package public API.
- §§2.21/2.23: preserve valid old behavior tests, migrate their wiring to the public API, cover changed
  lifecycle/concurrency/validation decisions. The derived constitution has no mandatory test-first cadence.
- §2.25: remove the four superseded Web implementations and migrate application wiring in the same PR.
  The later thin-host leaf still owns optional-adapter/module roster consolidation.
- Elsa §E5: existing version/artifact identity, no release/publisher change. Update inventory and generated
  solution filters for the new project, never alter the historical consolidation manifest.
- ADR0037: runtime allowlist remains secret-free; Web's current Studio bearer/bridge registration remains
  until #3358. The base pipeline supports normal host-supplied authentication/authorization.
- No reliance on provisional constitution sections and no new exception.

## Design

Implement AddElsaStudio, StudioBuilder.ConfigureShells, UseElsaStudio and MapElsaStudio from the accepted
[contract](../../docs/reports/studio-hosting-contract.md). Add public StudioHostingOptions and a lease-only
StudioConfigurationMutationGate. Bind then callback, clone installed settings, register once per root and
reject duplicate registration. Load a required shell file only when ShellsFile is non-null; its relative path
resolves against ContentRoot. The null mode retains host-owned configuration and creates no file.

AddCShellsAspNetCore includes the Api feature assembly and configuration provider. Configure WebRouting,
then explicitly compose WebRoutingShellResolver plus a selected-shell fallback. Caller framework callbacks
run after defaults and retain the framework's normal override semantics. A caller replacing the resolver
pipeline owns that replacement. Validate the selected provided blueprint at startup by composing it and
requiring explicit StudioApi enablement (case-insensitive), without activating feature services.

UseElsaStudio activates the selected shell before routing/authentication/authorization, then installs static
files. Missing/unavailable/failed activation remains best-effort for static/runtime requests; request
cancellation propagates. MapElsaStudio maps runtime JS, MapShells, strongly prioritized discovery endpoints,
unknown-API 404 and configured fallback. #3358 adds the bridge mappings/owned security registrations here.

Discovery acquires IShell.BeginScope and retains it across the entire async manifest operation. If only scope
acquisition races with disposal, look up the generation and retry once. Never retry arbitrary handler failures
or return an untracked provider. Respect the framework drain deadline. Wrap composition/startup errors with
hosting context; do not hide cancellation or mutate framework drain semantics.

Move RuntimeScript/AssetCaching/Activation/ActiveShellStudioApi from Web, preserving wire/static behavior.
Runtime settings continue to use an explicit allowlist; freeze the installed auth-enabled posture so later
configuration changes do not advertise a different gate. Web supplies its existing root Studio auth/bridge,
Nuplane/theme registrations and explicit distribution assemblies. Its Console Stream capture remains early.

The mutation coordinator grants only AcquireAsync(CancellationToken) -> IAsyncDisposable. It owns a private
semaphore and idempotent release handle, with no public Release/Dispose. Existing Web writers borrow the
registered coordinator over their whole read/revision/write/reload transaction; keep their private IO helper.
Optional adapters later borrow this exact instance. Tests use public builder/DI access, not private friends.

## Project structure

- `Studio/src/essentials/Elsa.Studio.Hosting/`: project, public composition/options/gate and internal behavior.
- `Studio/src/apps/Elsa.Studio.Web/`: replace extracted wiring with public Add/Use/Map; retain distribution and
  separately scoped optional behavior. Update existing writers only to borrow the common coordinator.
- `Studio/tests/Elsa.Studio.Tests/`: public Hosting test factory, third-party fixture features and HTTP/lifetime
  cases; migrate direct-internal runtime/static tests. Existing module-specific tests remain.
- `Studio/Elsa.Studio.slnx`, solution filters, `tools/ci/studio-package-identities.json`, reached lock files: current
  project inventory and dependency graph. Historical layout provenance stays frozen.
- This spec directory: bounded design, research, contracts and validation guide.

## Delivery

Run whole Studio tests with discovery/execution count reconciliation, actual static/runtime HTTP assertions,
tracked-scope replacement and negative controls. Re-run architecture graph restore/guards, locked solution
restore and map checks. Publish one draft PR for #3357, review through three independent lenses, post exact-head
gate evidence, mark ready after Build & test passes, request Code Review and verify the actual merged main.
Full NuGet-only final browser journeys remain on their existing dependent leaves.
