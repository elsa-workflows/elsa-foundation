# Implementation Plan: Shared Nuplane Adapter Adoption

**Branch**: `2340-shared-nuplane-adoption` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)
**Status**: Draft — source/discovery design prepared; the bounded diagnostic confirmed the catalog-refresh exception-observability gap. The upstream correction, tests and package qualification remain outstanding.

## Summary

Replace the two host package providers/observers with the public upstream adapter. Retain app-local standard monitored-option mappings and Elsa diagnostic callbacks. Preserve existing test assertions and final stable adoption gates. See [research](research.md) for source evidence and the one engineering prerequisite under investigation.

## Technical Context

**Language/Version**: Existing C#/.NET 10 host projects; upstream family targets .NET 8/9/10.
**Primary Dependencies**: CShells.Nuplane, Nuplane autoload, standard configuration/options/change-token and logging abstractions. No new Elsa feature-management dependency in Foundation.Host.
**Storage**: No new store or state machine. Existing upstream coordinator owns pending freshness/reload state.
**Testing**: xUnit Modularity regressions, Cluster/EF real-host refusal tests, architecture guards, backend E2E and `tools/demo/rehearse.sh` Acts1/2. Preserve existing test subjects/objectives and assertions when rewiring.
**Target Platform**: Existing supported host platforms; package assets retain all three upstream frameworks.
**Project Type**: Two existing deployable host applications using one optional upstream integration package.
**Performance Goals**: No performance measurements; owner retired that program. Keep Workbench changed-or-pending and deferred cold freshness behavior.
**Constraints**: Final source merge/adoption requires exact stable-family pins/locks/maps and actual rebuilt-host evidence. Interim source preparation/isolated previews are not acceptance.
**Scale/Scope**: Foundation #2314 only; no startup/readiness, readability, host-library, unloading or pruning implementation.

## Constitution Check

- Framework §2.7: upstream optional adapter isolates reusable behavior; Elsa-specific refusal interpretation remains local.
- Framework §2.16/§2.17: no new project; preserve feature-free host closure and accept a few intentionally different profile-mapping lines.
- Framework §2.21.1 and Elsa §E1: existing regression methods/assertions retained; repair fixture composition, not expected outcomes. The source-observed Error-versus-Warning gap must be resolved before source implementation is ready.
- Framework §2.23: exercise each new logic-bearing options setup through its public surface and actual DI composition, including all default/invalid/configuration-reload and refusal branches. Use public sealed setup implementations in nonpackable app projects; no new feature class.
- Existing host boundaries: preserve feed/base-path setup, observer order, host-provided assemblies, unrelated sharing and per-host startup policy. Do not relax architecture allowlists to admit a convenience helper dependency.
- Draft framework §2.24 and Elsa §E2.9 are not relied on as new ratified gates.

**Pre-design assessment**: no product-policy ambiguity in this observer-only scope. **Post-design assessment**: the bounded public-package diagnostic and independent review confirmed the existing catalog-refresh logging gap and selected log-and-rethrow correction. Planning remains Draft pending implementation, actual-DI tests and package qualification; no implementation-ready task has been created yet.

## Project Structure

### Documentation

`specs/199-shared-nuplane-adoption/`: spec, plan, research, data model, contracts, quickstart, then tasks after design readiness.

### Source Code

- `src/apps/Elsa.Foundation.Host/Program.cs`, host project and app-local options/diagnostic setup under `Shells/`.
- `src/apps/Elsa.Workbench/Program.cs`, host project and app-local setup under `Modularity/`.
- Remove duplicated `src/apps/Elsa.Foundation.Host/Feed/NuplaneAssemblyProvider.cs`, `src/apps/Elsa.Workbench/NuplaneAssemblyProvider.cs` and old observer implementations after composition and tests prove replacement. Preserve `ShellReloadFailure` and existing refusal models/endpoints.
- `tests/essentials/Modularity/Tests/WorkbenchShellCatalogRefreshTests.cs`, `HostOwnedServicesAreSharedWithShellsTests.cs`, focused profile/reload-token tests; retain relevant `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReloadRefusalTests.cs` and affected architecture coverage.
- Final adoption additionally updates `Directory.Packages.props`, reached `packages.lock.json`, applicable Docker restore graph and generated maps together after stable releases.

**Structure Decision**: host-specific options/diagnostics stay app-local; only reusable discovery/coordination is shared upstream. The existing feature-projection integration remains untouched. See [research](research.md).

## Validation Sequence

1. Implement and qualify the upstream exception-observability correction. The completed public-package diagnostic confirmed that catalog-refresh failure currently produces only Nuplane’s generic Warning. The manually constructed coordinator must receive the optional DI logger from the holder factory; actual-DI capture tests must prove Error logging with the original exception plus dispatcher Warning/later-observer delivery, while preserving the existing thrown-exception contract.
2. Finalize design/contracts/tasks, commit the reviewed plan, and implement bounded app-local profiles and upstream composition in the owned preparation worktree.
3. Qualify in a separate copy with exact audited previews; retain existing assertions, add runtime configuration reload, cold Begin freshness, one coordinator identity and refusal/redaction tests. Run scoped builds only through the shared build-slot wrapper.
4. Root reviews the complete diff and delegated work. Run causal reversible mutations for live option invalidation and alias/order behavior where applicable. Preserve unsuccessful attempts honestly.
5. Publish prepared organization branch with exact evidence; keep final stable merge/adoption task unchecked.
6. Once required upstream work is released, integrate exact stable family/pins/locks/maps, rebuild real hosts and execute named regression, backend E2E and frozen Acts1/2 demo proof. Complete current-head and resulting-main gates before closure.

## Complexity Tracking

No constitutional exception is approved. The Error logging mismatch is recorded for correction, not waived. No blanket permission or review gate is introduced.
