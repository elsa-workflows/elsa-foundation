# Feature Specification: Studio shared loader integration

**Feature Branch**: `agent/codex/3356-studio-shared-loader`

**Created**: 2026-10-26

**Status**: Implemented — implementation PR [#3379](https://github.com/elsa-workflows/elsa-foundation/pull/3379)

**Input**: [Task #3356](https://github.com/elsa-workflows/elsa-foundation/issues/3356),
part of [Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340).
Boundary decisions are in the [hosting contract](../../docs/reports/studio-hosting-contract.md).

## User scenarios and testing

### US1 — Installed modules become available (P1)

A Studio host operator installs, updates or removes a module without restarting the host.
The serving shell uses the current module generation, while already admitted work can finish.

1. Given a cold host and an installed module, first activation discovers its feature.
2. Given an active shell, adding or updating a package refreshes discovery before replacing
   the serving generation. A retained old-generation scope remains usable.
3. Given the last installed module, removal empties package discovery and removes its new-shell
   services without breaking an already retained scope.

Verify through the actual Studio composition and shared adapter with real Nuplane directory-feed
packages, plus targeted catalog tests where failure injection is cheaper.

### US2 — Reload failures stay diagnosable and retryable (P1)

An operator can identify a failed shell reload, and later reconciliation retries pending work.
An unchanged completion with no pending work does not refresh or reload. Requested cancellation
is propagated; shutdown owns outstanding work. Console Stream stays rooted in the host.

Verify returned reload error reporting and pending retry through the adapter's public options and
registry; exercise cancellation and host shutdown without a detached Studio background reload task.

### US3 — Module metadata remains consumable (P2)

A module author uses the same qualified manifest contracts and generator as Foundation.
Generated `elsa-package.json` and Studio's feature catalog retain their wire meaning.
Verify generated module manifests and existing catalog/compatibility tests after migration.

## Requirements

- **FR-001**: Align Studio and reached Studio extension projects to CShells 0.0.31,
  Nuplane 0.0.12 and Elsa.Specifications manifest contracts/generator preview.8.
- **FR-002**: Use CShells.Nuplane `WithNuplaneFeatureDiscovery`, `ChangedOrPending`, automatic
  reload enabled, and its existing result callback for Studio diagnostics.
- **FR-003**: Delete redundant Studio assembly-provider, detached reload observer and catalog
  refresher wrappers. Use the existing runtime catalog directly for Studio HTTP reads.
- **FR-004**: Keep in-box feature discovery, browser endpoints, authentication and root-lived
  Console Stream composition intact. Do not extract production packages in this task.
- **FR-005**: Update reached NuGet locks; do not upgrade unrelated libraries or activate publishing.

## Success criteria

Install/update/removal-to-empty pass through the production adapter profile; failed reload reporting
and pending retry are observed; affected Studio tests and manifest checks pass; the three local
wrappers are gone. A negative control disabling automatic reload must fail generation replacement.

## Assumptions

The released APIs qualified in [the reuse report](../../docs/reports/studio-hosting-reuse.md) remain
the selected dependency set. Package-store pruning and the full NuGet-only consumer journeys belong
to #3359 and #3363/#3364, respectively. This task does not claim physical unload or pruning proof.
